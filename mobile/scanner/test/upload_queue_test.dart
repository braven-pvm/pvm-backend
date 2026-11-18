import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/queue/upload_queue.dart';

/// Records every upload and answers with the next scripted outcome.
class FakeUploader implements DocumentUploader {
  FakeUploader(this.outcomes);

  final List<UploadOutcome> outcomes;
  final List<({String clientDocumentId, String documentType, int pageCount, String? staffId, bool fileExisted})> calls =
      [];

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  }) async {
    calls.add((
      clientDocumentId: clientDocumentId,
      documentType: documentType,
      pageCount: pageCount,
      staffId: staffId,
      fileExisted: pdf.existsSync(),
    ));
    final index = calls.length - 1;
    return index < outcomes.length ? outcomes[index] : outcomes.last;
  }
}

void main() {
  late Directory root;
  late Directory queueDirectory;

  setUp(() {
    root = Directory.systemTemp.createTempSync('upload_queue_test');
    queueDirectory = Directory('${root.path}/queue');
  });

  tearDown(() {
    root.deleteSync(recursive: true);
  });

  File scannedPdf([String name = 'scan.pdf']) {
    final file = File('${root.path}/$name');
    file.writeAsBytesSync('%PDF-1.7 test'.codeUnits);
    return file;
  }

  Future<UploadQueue> openQueue(
    DocumentUploader uploader, {
    List<Duration>? delays,
    void Function(UploadQueue queue)? onDelay,
  }) async {
    late UploadQueue queue;
    queue = await UploadQueue.open(
      directory: queueDirectory,
      uploader: uploader,
      delay: (duration) async {
        delays?.add(duration);
        onDelay?.call(queue);
      },
    );
    return queue;
  }

  test('enqueue_persists_item_and_file_across_restart', () async {
    final first = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final queued = await first.enqueue(scannedPdf(), 'POD', 2);
    await first.close();

    expect(queued.state, QueuedDocument.waiting);
    expect(queued.clientDocumentId, matches(RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')));

    final second = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final items = await second.items.first;

    expect(items, hasLength(1));
    expect(items.single.clientDocumentId, queued.clientDocumentId);
    expect(items.single.documentType, 'POD');
    expect(items.single.pageCount, 2);
    expect(items.single.state, QueuedDocument.waiting);
    expect(second.pdfOf(items.single).existsSync(), isTrue);
    await second.close();
  });

  test('upload_success_marks_received_and_deletes_local_pdf', () async {
    final uploader = FakeUploader([const UploadAccepted(documentId: 'doc-1', state: 'Received')]);
    final queue = await openQueue(uploader);
    final source = scannedPdf();

    final queued = await queue.enqueue(source, 'POD', 1);
    final pdf = queue.pdfOf(queued);
    queue.start();
    await queue.whenIdle;

    final item = queue.current.single;
    expect(item.state, QueuedDocument.received);
    expect(item.documentId, 'doc-1');
    expect(uploader.calls.single.fileExisted, isTrue);
    expect(pdf.existsSync(), isFalse);
    await queue.close();
  });

  test('upload_failure_retries_with_backoff_and_keeps_pdf', () async {
    final uploader = FakeUploader([const UploadRetryLater('The network is not available.')]);
    final delays = <Duration>[];
    final queue = await openQueue(
      uploader,
      delays: delays,
      onDelay: (queue) {
        if (delays.length == 3) {
          queue.close();
        }
      },
    );

    final queued = await queue.enqueue(scannedPdf(), 'POD', 1);
    queue.start();
    await queue.whenIdle;

    expect(uploader.calls, hasLength(3));
    expect(delays, hasLength(3));
    expect(delays[1], greaterThan(delays[0]));
    expect(delays[2], greaterThan(delays[1]));
    final item = queue.current.single;
    expect(item.state, QueuedDocument.waiting);
    expect(item.attempts, 3);
    expect(item.lastError, 'The network is not available.');
    expect(queue.pdfOf(queued).existsSync(), isTrue);
  });

  test('retry_sends_same_clientDocumentId', () async {
    final uploader = FakeUploader([
      const UploadRetryLater('Server error.'),
      const UploadAccepted(documentId: 'doc-2', state: 'Received'),
    ]);
    final queue = await openQueue(uploader, delays: []);

    final queued = await queue.enqueue(scannedPdf(), 'POD', 3);
    queue.start();
    await queue.whenIdle;

    expect(uploader.calls, hasLength(2));
    expect(uploader.calls[0].clientDocumentId, queued.clientDocumentId);
    expect(uploader.calls[1].clientDocumentId, queued.clientDocumentId);
    expect(uploader.calls[1].pageCount, 3);
    expect(queue.current.single.state, QueuedDocument.received);
    await queue.close();
  });

  test('rejected_upload_is_failed_with_reason_and_keeps_pdf_until_removed', () async {
    final uploader = FakeUploader([const UploadRejected('The file is not a PDF.')]);
    final queue = await openQueue(uploader);

    final queued = await queue.enqueue(scannedPdf(), 'POD', 1);
    queue.start();
    await queue.whenIdle;

    final item = queue.current.single;
    expect(item.state, QueuedDocument.failed);
    expect(item.lastError, 'The file is not a PDF.');
    expect(uploader.calls, hasLength(1));
    expect(queue.pdfOf(queued).existsSync(), isTrue);

    await queue.remove(queued.clientDocumentId);
    expect(queue.current, isEmpty);
    expect(queue.pdfOf(queued).existsSync(), isFalse);
    await queue.close();
  });

  test('remove_is_refused_for_an_item_that_is_not_failed', () async {
    final queue = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final queued = await queue.enqueue(scannedPdf(), 'POD', 1);

    expect(() => queue.remove(queued.clientDocumentId), throwsStateError);
    await queue.close();
  });

  test('device_revoked_holds_the_queue_keeps_the_item_and_pdf_and_resume_uploads_again', () async {
    final uploader = FakeUploader([
      const UploadDeviceRevoked(),
      const UploadAccepted(documentId: 'doc-3', state: 'Received'),
    ]);
    final queue = await openQueue(uploader);

    final queued = await queue.enqueue(scannedPdf(), 'POD', 1);
    await queue.enqueue(scannedPdf('second.pdf'), 'POD', 1);
    queue.start();
    await queue.whenIdle;

    // The loop stops at the first 401: the second item is not tried.
    expect(uploader.calls, hasLength(1));
    expect(queue.hold.value, QueueHold.deviceRevoked);
    expect(queue.current.map((item) => item.state), everyElement(QueuedDocument.waiting));
    expect(queue.current.first.attempts, 0);
    expect(queue.pdfOf(queued).existsSync(), isTrue);

    queue.resume();
    await queue.whenIdle;

    expect(queue.hold.value, isNull);
    expect(queue.current.map((item) => item.state), everyElement(QueuedDocument.received));
    await queue.close();
  });

  test('pause_stops_the_loop_and_a_paused_queue_still_takes_scans', () async {
    final uploader = FakeUploader([const UploadRetryLater('offline')]);
    final queue = await openQueue(uploader, onDelay: (queue) => queue.pause(QueueHold.deviceReset));

    final first = await queue.enqueue(scannedPdf('a.pdf'), 'POD', 1);
    queue.start();
    await queue.whenIdle;
    final second = await queue.enqueue(scannedPdf('b.pdf'), 'POD', 2, staffId: 's-1');
    await queue.whenIdle;

    expect(uploader.calls, hasLength(1));
    expect(queue.hold.value, QueueHold.deviceReset);
    expect(queue.current.map((item) => item.clientDocumentId), [first.clientDocumentId, second.clientDocumentId]);
    expect(queue.current.last.staffId, 's-1');
    expect(queue.pdfOf(second).existsSync(), isTrue);
    await queue.close();
  });

  test('the_staff_id_of_the_scan_goes_with_the_upload_also_after_a_restart', () async {
    final first = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final queued = await first.enqueue(scannedPdf(), 'POD', 1, staffId: 'staff-7');
    final unnamed = await first.enqueue(scannedPdf('b.pdf'), 'POD', 1);
    await first.close();

    expect(queued.staffId, 'staff-7');
    expect(unnamed.staffId, isNull);

    final uploader = FakeUploader([const UploadAccepted(documentId: 'doc', state: 'Received')]);
    final second = await openQueue(uploader);
    second.start();
    await second.whenIdle;

    expect(uploader.calls, hasLength(2));
    final byId = {for (final call in uploader.calls) call.clientDocumentId: call.staffId};
    expect(byId[queued.clientDocumentId], 'staff-7');
    expect(byId[unnamed.clientDocumentId], isNull);
    await second.close();
  });

  test('a_queue_file_from_before_staff_names_still_reads', () async {
    queueDirectory.createSync(recursive: true);
    Directory('${queueDirectory.path}/pdfs').createSync();
    const id = '3f2b1c4d-0000-4000-8000-000000000001';
    File('${queueDirectory.path}/pdfs/$id.pdf').writeAsStringSync('%PDF-1.7');
    File('${queueDirectory.path}/queue.json').writeAsStringSync(jsonEncode([
      {
        'clientDocumentId': id,
        'documentType': 'POD',
        'pageCount': 1,
        'state': 'waiting',
        'createdAt': '2026-11-17T08:00:00.000Z',
        'attempts': 0,
      },
    ]));

    final queue = await openQueue(FakeUploader([const UploadRetryLater('offline')]));

    expect(queue.current.single.clientDocumentId, id);
    expect(queue.current.single.staffId, isNull);
    await queue.close();
  });

  test('uploads_one_item_at_a_time_oldest_first', () async {
    final gate = Completer<void>();
    var active = 0;
    var maxActive = 0;
    final order = <String>[];
    final uploader = _GatedUploader((clientDocumentId) async {
      active++;
      maxActive = active > maxActive ? active : maxActive;
      order.add(clientDocumentId);
      await gate.future;
      active--;
      return const UploadAccepted(documentId: 'x', state: 'Received');
    });
    final queue = await openQueue(uploader);

    final first = await queue.enqueue(scannedPdf('a.pdf'), 'POD', 1);
    final second = await queue.enqueue(scannedPdf('b.pdf'), 'POD', 1);
    queue.start();
    await Future<void>.delayed(const Duration(milliseconds: 20));
    gate.complete();
    await queue.whenIdle;

    expect(maxActive, 1);
    expect(order, [first.clientDocumentId, second.clientDocumentId]);
    final newestFirst = await queue.items.first;
    expect(newestFirst.first.clientDocumentId, second.clientDocumentId);
    await queue.close();
  });

  test('an_uploader_error_is_retried_later_not_lost', () async {
    var calls = 0;
    final uploader = _GatedUploader((_) async {
      calls++;
      if (calls == 1) {
        throw StateError('unexpected');
      }
      return const UploadAccepted(documentId: 'doc-4', state: 'Received');
    });
    final delays = <Duration>[];
    final queue = await openQueue(uploader, delays: delays);

    await queue.enqueue(scannedPdf(), 'POD', 1);
    queue.start();
    await queue.whenIdle;

    expect(calls, 2);
    expect(delays, hasLength(1));
    expect(queue.current.single.state, QueuedDocument.received);
    await queue.close();
  });

  test('concurrent_enqueues_and_uploads_leave_a_readable_queue_file', () async {
    final uploader = FakeUploader([const UploadAccepted(documentId: 'doc', state: 'Received')]);
    final queue = await openQueue(uploader);
    queue.start();

    final enqueued = await Future.wait([
      for (var i = 0; i < 20; i++) queue.enqueue(scannedPdf('scan-$i.pdf'), 'POD', 1),
    ]);
    await queue.whenIdle;
    await queue.close();

    expect(enqueued, hasLength(20));
    expect(uploader.calls, hasLength(20));
    expect(queue.current.where((item) => item.state == QueuedDocument.received), hasLength(20));
    final saved = jsonDecode(File('${queueDirectory.path}/queue.json').readAsStringSync()) as List<dynamic>;
    expect(saved, hasLength(20));
    expect(saved.every((entry) => (entry as Map<String, dynamic>)['state'] == QueuedDocument.received), isTrue);

    final reopened = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    expect(reopened.current, isEmpty);
    await reopened.close();
  });

  test('a_corrupt_queue_file_is_restored_from_the_backup', () async {
    final first = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final queued = await first.enqueue(scannedPdf(), 'POD', 2);
    await first.close();
    File('${queueDirectory.path}/queue.json').copySync('${queueDirectory.path}/queue.json.bak');
    File('${queueDirectory.path}/queue.json').writeAsStringSync('[{"clientDocumentId": "trunc');

    final second = await openQueue(FakeUploader([const UploadRetryLater('offline')]));

    expect(second.current.single.clientDocumentId, queued.clientDocumentId);
    expect(second.current.single.pageCount, 2);
    expect(second.current.single.state, QueuedDocument.waiting);
    await second.close();
  });

  test('a_corrupt_queue_file_without_backup_adopts_the_pdfs', () async {
    final pdfs = Directory('${queueDirectory.path}/pdfs')..createSync(recursive: true);
    File('${pdfs.path}/0b6f3c3e-9a51-4c38-9d0e-2b8f5a1c7d11.pdf').writeAsBytesSync('%PDF-1.7 a'.codeUnits);
    File('${pdfs.path}/5d2a9e7b-1c44-4f6a-8e3b-7a9c0d4e2f22.pdf').writeAsBytesSync('%PDF-1.7 b'.codeUnits);
    File('${queueDirectory.path}/queue.json').writeAsStringSync('not json');

    final queue = await openQueue(FakeUploader([const UploadRetryLater('offline')]));

    expect(queue.current, hasLength(2));
    for (final item in queue.current) {
      expect(item.state, QueuedDocument.waiting);
      expect(item.documentType, 'POD');
      expect(item.pageCount, greaterThanOrEqualTo(1));
      expect(queue.pdfOf(item).existsSync(), isTrue);
    }
    await queue.close();
  });

  test('a_pdf_no_item_references_is_adopted_and_referenced_pdfs_are_not_duplicated', () async {
    final first = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    await first.enqueue(scannedPdf(), 'POD', 1);
    await first.close();
    File('${queueDirectory.path}/pdfs/7e1d2c3b-4a5f-4e6d-9c8b-1a2b3c4d5e6f.pdf').writeAsBytesSync('%PDF-1.7 c'.codeUnits);

    final second = await openQueue(FakeUploader([const UploadRetryLater('offline')]));

    expect(second.current, hasLength(2));
    expect(second.current.map((item) => item.clientDocumentId).toSet(), hasLength(2));
    await second.close();
  });

  test('a_waiting_item_whose_pdf_is_gone_after_an_attempt_is_received_unconfirmed', () async {
    final first = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    final queued = await first.enqueue(scannedPdf(), 'POD', 1);
    await first.close();
    final saved = jsonDecode(File('${queueDirectory.path}/queue.json').readAsStringSync()) as List<dynamic>;
    (saved.single as Map<String, dynamic>)['attempts'] = 1;
    File('${queueDirectory.path}/queue.json').writeAsStringSync(jsonEncode(saved));
    first.pdfOf(queued).deleteSync();

    final second = await openQueue(FakeUploader([const UploadRetryLater('offline')]));

    expect(second.current.single.state, QueuedDocument.received);
    expect(second.current.single.note, 'Sent earlier');
    await second.close();
  });

  test('a_retryable_failure_moves_the_item_behind_the_other_waiting_items', () async {
    final order = <String>[];
    late QueuedDocument first;
    final uploader = _GatedUploader((clientDocumentId) async {
      order.add(clientDocumentId);
      if (clientDocumentId == first.clientDocumentId && order.length == 1) {
        return const UploadRetryLater('Server error.');
      }
      return const UploadAccepted(documentId: 'x', state: 'Received');
    });
    final queue = await openQueue(uploader, delays: []);

    first = await queue.enqueue(scannedPdf('a.pdf'), 'POD', 1);
    final second = await queue.enqueue(scannedPdf('b.pdf'), 'POD', 1);
    queue.start();
    await queue.whenIdle;

    expect(order, [first.clientDocumentId, second.clientDocumentId, first.clientDocumentId]);
    final newestFirst = await queue.items.first;
    expect(newestFirst.map((item) => item.clientDocumentId), [second.clientDocumentId, first.clientDocumentId]);
    await queue.close();
  });

  test('close_waits_for_the_running_upload_and_nothing_saves_after_it', () async {
    final gate = Completer<UploadOutcome>();
    final queue = await openQueue(_GatedUploader((_) => gate.future));
    await queue.enqueue(scannedPdf(), 'POD', 1);
    queue.start();
    await Future<void>.delayed(const Duration(milliseconds: 20));

    var closed = false;
    final closing = queue.close().then((_) => closed = true);
    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(closed, isFalse);

    gate.complete(const UploadAccepted(documentId: 'doc-5', state: 'Received'));
    await closing;
    final stateFile = File('${queueDirectory.path}/queue.json');
    final savedAtClose = stateFile.readAsStringSync();
    expect(savedAtClose, contains(QueuedDocument.received));

    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(stateFile.readAsStringSync(), savedAtClose);
    expect(() => queue.enqueue(scannedPdf('late.pdf'), 'POD', 1), throwsStateError);
  });

  test('an_item_left_uploading_by_a_crash_is_waiting_after_restart', () async {
    final first = await openQueue(_GatedUploader((_) => Completer<UploadOutcome>().future));
    await first.enqueue(scannedPdf(), 'POD', 1);
    first.start();
    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(first.current.single.state, QueuedDocument.uploading);

    final second = await openQueue(FakeUploader([const UploadRetryLater('offline')]));
    expect(second.current.single.state, QueuedDocument.waiting);
    await second.close();
  });
}

class _GatedUploader implements DocumentUploader {
  _GatedUploader(this.onUpload);

  final Future<UploadOutcome> Function(String clientDocumentId) onUpload;

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  }) =>
      onUpload(clientDocumentId);
}
