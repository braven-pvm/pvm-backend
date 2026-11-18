import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../api/device_api.dart';

/// One scanned document on its way to the backend.
@immutable
class QueuedDocument {
  const QueuedDocument({
    required this.clientDocumentId,
    required this.documentType,
    required this.pageCount,
    required this.state,
    required this.createdAt,
    this.attempts = 0,
    this.lastError,
    this.documentId,
    this.note,
    this.staffId,
    this.title,
    this.userNote,
    this.hasThumbnail = false,
  });

  /// The longest title the backend takes (GENERAL only).
  static const maxTitleLength = 120;

  /// The longest note the backend takes.
  static const maxNoteLength = 1000;

  /// The document type that may have a title.
  static const generalType = 'GENERAL';

  static const waiting = 'waiting';
  static const uploading = 'uploading';
  static const received = 'received';
  static const failed = 'failed';

  /// Generated on the phone and sent with every attempt; the backend ignores a second copy.
  final String clientDocumentId;
  final String documentType;
  final int pageCount;

  /// [waiting], [uploading], [received] or [failed].
  final String state;
  final DateTime createdAt;
  final int attempts;

  /// The reason of the last failed attempt, in plain language.
  final String? lastError;

  /// The backend document ID, once received.
  final String? documentId;

  /// A short remark for the user, for example "Sent earlier".
  final String? note;

  /// The staff name chosen when this document was scanned; sent with every upload attempt.
  final String? staffId;

  /// The title the user typed (general documents only), or null.
  final String? title;

  /// The note the user typed, or null. Not the same as [note], the app's own remark.
  final String? userNote;

  /// Whether a JPEG of the first page is kept with the item ([UploadQueue.thumbnailOf]).
  final bool hasThumbnail;

  /// Waiting again for a new attempt, without the last reason.
  QueuedDocument _waitingAgain() => QueuedDocument(
        clientDocumentId: clientDocumentId,
        documentType: documentType,
        pageCount: pageCount,
        state: waiting,
        createdAt: createdAt,
        attempts: attempts,
        documentId: documentId,
        note: note,
        staffId: staffId,
        title: title,
        userNote: userNote,
        hasThumbnail: hasThumbnail,
      );

  QueuedDocument copyWith({String? state, int? attempts, String? lastError, String? documentId, String? note}) =>
      QueuedDocument(
        clientDocumentId: clientDocumentId,
        documentType: documentType,
        pageCount: pageCount,
        state: state ?? this.state,
        createdAt: createdAt,
        attempts: attempts ?? this.attempts,
        lastError: lastError ?? this.lastError,
        documentId: documentId ?? this.documentId,
        note: note ?? this.note,
        staffId: staffId,
        title: title,
        userNote: userNote,
        hasThumbnail: hasThumbnail,
      );

  Map<String, Object?> toJson() => {
        'clientDocumentId': clientDocumentId,
        'documentType': documentType,
        'pageCount': pageCount,
        'state': state,
        'createdAt': createdAt.toUtc().toIso8601String(),
        'attempts': attempts,
        'lastError': lastError,
        'documentId': documentId,
        'note': note,
        if (staffId != null) 'staffId': staffId,
        if (title != null) 'title': title,
        if (userNote != null) 'userNote': userNote,
        if (hasThumbnail) 'thumbnail': true,
      };

  factory QueuedDocument.fromJson(Map<String, dynamic> json) => QueuedDocument(
        clientDocumentId: json['clientDocumentId'] as String,
        documentType: json['documentType'] as String,
        pageCount: json['pageCount'] as int,
        state: json['state'] as String,
        createdAt: DateTime.parse(json['createdAt'] as String),
        attempts: json['attempts'] as int? ?? 0,
        lastError: json['lastError'] as String?,
        documentId: json['documentId'] as String?,
        note: json['note'] as String?,
        staffId: json['staffId'] as String?,
        title: json['title'] as String?,
        userNote: json['userNote'] as String?,
        hasThumbnail: json['thumbnail'] == true,
      );
}

/// Why the queue stopped uploading until the user acts.
enum QueueHold {
  /// The backend answered 401: the device was removed or set up again. The items wait for a
  /// new enrolment.
  deviceRevoked,

  /// "Reset this phone": the key was deleted on purpose. The items wait for a new enrolment.
  deviceReset,
}

/// Default retry delay: 5 s, 10 s, 20 s, ... up to 5 minutes.
Duration defaultBackoff(int attempt) {
  final seconds = 5 * math.pow(2, math.max(0, attempt - 1)).toInt();
  return Duration(seconds: math.min(seconds, 300));
}

/// A persistent upload queue.
///
/// Each PDF is copied into `directory/pdfs/<clientDocumentId>.pdf` and the item list is kept in
/// `directory/queue.json` (the previous good copy in `queue.json.bak`), so nothing is lost when
/// the app stops. Saves run one at a time. Items upload one at a time; an item that failed and
/// may succeed later goes behind the other waiting items and is retried with backoff and the
/// same client document ID. The local PDF is deleted only after the receipt is saved. A rejected
/// upload (400 or 413) is marked [QueuedDocument.failed] and keeps its PDF. A 401 holds the queue
/// ([QueueHold.deviceRevoked]); the items and PDFs stay for the next enrolment.
class UploadQueue {
  UploadQueue._(this._directory, this._uploader, this._backoff, this._delay, this._items);

  static const _stateFile = 'queue.json';
  static const _backupFile = 'queue.json.bak';
  static const _pdfFolder = 'pdfs';
  static const _thumbnailFolder = 'thumbs';
  static final _guid = RegExp(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$');

  final Directory _directory;
  final DocumentUploader _uploader;
  final Duration Function(int attempt) _backoff;
  final Future<void> Function(Duration) _delay;
  final List<QueuedDocument> _items;

  final _changes = StreamController<List<QueuedDocument>>.broadcast();
  final _hold = ValueNotifier<QueueHold?>(null);
  var _started = false;
  var _closed = false;
  Future<void>? _loop;
  Completer<void>? _wake;
  Future<void> _saveChain = Future<void>.value();

  /// Opens the queue in [directory]. It never fails because of a bad queue file:
  /// - an unreadable `queue.json` is replaced by `queue.json.bak`, or else by nothing;
  /// - a PDF in the queue folder that no item references becomes a waiting item
  ///   ([orphanDocumentType]); its file name is kept as the client document ID when it is a
  ///   GUID, so the backend can still recognise a second copy;
  /// - an item left uploading by a stopped app is waiting again;
  /// - a waiting item whose PDF is gone after an attempt was sent earlier (the receipt was not
  ///   saved); it shows as received with the note "Sent earlier";
  /// - received items from an earlier run are dropped.
  static Future<UploadQueue> open({
    required Directory directory,
    required DocumentUploader uploader,
    Duration Function(int attempt) backoff = defaultBackoff,
    Future<void> Function(Duration)? delay,
    String orphanDocumentType = 'POD',
  }) async {
    final pdfFolder = Directory('${directory.path}/$_pdfFolder');
    await pdfFolder.create(recursive: true);

    final stateFile = File('${directory.path}/$_stateFile');
    var saved = await _read(stateFile);
    if (saved == null) {
      saved = await _read(File('${directory.path}/$_backupFile')) ?? const <QueuedDocument>[];
      // Remove the bad file, so the next save does not keep it as the backup.
      try {
        if (await stateFile.exists()) {
          await stateFile.delete();
        }
      } on FileSystemException {
        // The next save replaces it.
      }
    }

    final items = <QueuedDocument>[];
    for (final stored in saved) {
      if (stored.state == QueuedDocument.received) {
        continue;
      }
      var item = stored.state == QueuedDocument.uploading ? stored.copyWith(state: QueuedDocument.waiting) : stored;
      if (item.state == QueuedDocument.waiting && !await _pdfPath(directory, item.clientDocumentId).exists()) {
        item = item.attempts > 0
            ? item.copyWith(state: QueuedDocument.received, note: 'Sent earlier')
            : item.copyWith(
                state: QueuedDocument.failed,
                lastError: 'The scan file is missing on this phone. Scan the paper again.',
              );
      }
      items.add(item);
    }

    await _adoptOrphans(directory, pdfFolder, items, orphanDocumentType);
    await _dropUnusedThumbnails(directory, items);

    final queue = UploadQueue._(directory, uploader, backoff, delay ?? Future<void>.delayed, items);
    try {
      await queue._save();
    } on FileSystemException {
      // The next save tries again; the items are in memory.
    }
    return queue;
  }

  static File _pdfPath(Directory directory, String clientDocumentId) =>
      File('${directory.path}/$_pdfFolder/$clientDocumentId.pdf');

  static File _thumbnailPath(Directory directory, String clientDocumentId) =>
      File('${directory.path}/$_thumbnailFolder/$clientDocumentId.jpg');

  /// Deletes the thumbnails of items that are gone (received in an earlier run, or removed).
  static Future<void> _dropUnusedThumbnails(Directory directory, List<QueuedDocument> items) async {
    final folder = Directory('${directory.path}/$_thumbnailFolder');
    final kept = {
      for (final item in items)
        if (item.hasThumbnail) '${item.clientDocumentId}.jpg',
    };
    try {
      if (!await folder.exists()) {
        return;
      }
      await for (final entry in folder.list()) {
        if (entry is File && !kept.contains(entry.uri.pathSegments.last)) {
          await entry.delete();
        }
      }
    } on FileSystemException {
      // The next start tries again.
    }
  }

  /// Trimmed text, cut to [maxLength]; null when blank.
  static String? _field(String? text, int maxLength) {
    final trimmed = text?.trim() ?? '';
    if (trimmed.isEmpty) {
      return null;
    }
    return trimmed.length <= maxLength ? trimmed : trimmed.substring(0, maxLength).trimRight();
  }

  /// The items of a queue file, or null when the file is missing or unreadable.
  static Future<List<QueuedDocument>?> _read(File file) async {
    try {
      if (!await file.exists()) {
        return null;
      }
      final list = jsonDecode(await file.readAsString()) as List<dynamic>;
      return [for (final entry in list) QueuedDocument.fromJson(entry as Map<String, dynamic>)];
    } on Object {
      return null; // Truncated or otherwise bad: the caller tries the next source.
    }
  }

  static Future<void> _adoptOrphans(
    Directory directory,
    Directory pdfFolder,
    List<QueuedDocument> items,
    String documentType,
  ) async {
    final referenced = {for (final item in items) item.clientDocumentId};
    final List<FileSystemEntity> entries;
    try {
      entries = await pdfFolder.list().toList();
    } on FileSystemException {
      return;
    }

    for (final entry in entries) {
      final name = entry.uri.pathSegments.last;
      if (entry is! File || !name.toLowerCase().endsWith('.pdf')) {
        continue;
      }
      final baseName = name.substring(0, name.length - 4);
      if (referenced.contains(baseName)) {
        continue;
      }

      try {
        var file = entry;
        var clientDocumentId = baseName;
        if (!_guid.hasMatch(baseName)) {
          clientDocumentId = const Uuid().v4();
          file = await file.rename(_pdfPath(directory, clientDocumentId).path);
        }
        items.add(QueuedDocument(
          clientDocumentId: clientDocumentId,
          documentType: documentType,
          pageCount: await _countPages(file),
          state: QueuedDocument.waiting,
          createdAt: await file.lastModified(),
          note: 'Recovered on this phone',
        ));
        referenced.add(clientDocumentId);
      } on FileSystemException {
        // Leave this file; the next start tries again.
      }
    }
  }

  /// A best-effort page count of a PDF; at least 1.
  static Future<int> _countPages(File pdf) async {
    try {
      final text = latin1.decode(await pdf.readAsBytes());
      return math.max(1, RegExp(r'/Type\s*/Page(?![s\w])').allMatches(text).length);
    } on Object {
      return 1;
    }
  }

  /// The items, newest first. A new listener gets the current list at once.
  Stream<List<QueuedDocument>> get items async* {
    yield _newestFirst();
    yield* _changes.stream;
  }

  /// The items in the order they upload.
  List<QueuedDocument> get current => List.unmodifiable(_items);

  /// Set while the queue waits for a new enrolment.
  ValueListenable<QueueHold?> get hold => _hold;

  /// Completes when the queue has nothing it can upload now (empty, held, or closed).
  Future<void> get whenIdle => _loop ?? Future<void>.value();

  /// The local PDF of [item]. It exists until the backend confirms receipt.
  File pdfOf(QueuedDocument item) => _pdfPath(_directory, item.clientDocumentId);

  /// The first-page JPEG of [item], or null when it has none.
  File? thumbnailOf(QueuedDocument item) =>
      item.hasThumbnail ? _thumbnailPath(_directory, item.clientDocumentId) : null;

  /// Adds a scanned PDF: copies it into the queue folder, saves the item, and only then deletes
  /// the scanner's original. Throws [FileSystemException] when the scan cannot be kept.
  ///
  /// [title] is kept for [QueuedDocument.generalType] only; [title] and [note] are trimmed, cut to
  /// the backend's limits, and dropped when blank. [thumbnail] (a JPEG of the first page) is
  /// optional: when it cannot be saved, the scan is kept without it.
  Future<QueuedDocument> enqueue(
    File pdf,
    String documentType,
    int pageCount, {
    String? staffId,
    String? title,
    String? note,
    Uint8List? thumbnail,
  }) async {
    if (_closed) {
      throw StateError('The upload queue is closed.');
    }

    final clientDocumentId = const Uuid().v4();
    var hasThumbnail = false;
    if (thumbnail != null && thumbnail.isNotEmpty) {
      try {
        final file = _thumbnailPath(_directory, clientDocumentId);
        await file.parent.create(recursive: true);
        await file.writeAsBytes(thumbnail, flush: true);
        hasThumbnail = true;
      } on FileSystemException {
        // The scan matters, not its picture.
      }
    }

    final item = QueuedDocument(
      clientDocumentId: clientDocumentId,
      documentType: documentType,
      pageCount: pageCount,
      state: QueuedDocument.waiting,
      createdAt: DateTime.now(),
      staffId: staffId,
      title: documentType == QueuedDocument.generalType ? _field(title, QueuedDocument.maxTitleLength) : null,
      userNote: _field(note, QueuedDocument.maxNoteLength),
      hasThumbnail: hasThumbnail,
    );
    final copy = pdfOf(item);

    try {
      await pdf.copy(copy.path);
      _items.add(item);
      await _save();
    } on Object {
      // Undo, so that a half-kept scan does not come back later; the original is untouched.
      _items.remove(item);
      _emit();
      try {
        await copy.delete();
      } on FileSystemException {
        // Nothing was copied.
      }
      await _deleteThumbnail(item);
      rethrow;
    }

    try {
      await pdf.delete();
    } on FileSystemException {
      // The scanner's own copy is in the cache folder; Android clears it later.
    }

    _kick();
    return item;
  }

  /// Removes a failed item and deletes its PDF. Only failed items can be removed.
  Future<void> remove(String clientDocumentId) async {
    final index = _items.indexWhere((item) => item.clientDocumentId == clientDocumentId);
    if (index < 0 || _items[index].state != QueuedDocument.failed) {
      throw StateError('Only a failed document can be removed.');
    }

    final item = _items.removeAt(index);
    await _save();
    final pdf = pdfOf(item);
    if (await pdf.exists()) {
      await pdf.delete();
    }
    await _deleteThumbnail(item);
  }

  /// "Try again" on a failed item: it waits for upload again, with the same client document ID.
  /// On a waiting item it ends the retry wait.
  Future<void> retry(String clientDocumentId) async {
    final index = _items.indexWhere((item) => item.clientDocumentId == clientDocumentId);
    if (index >= 0 && _items[index].state == QueuedDocument.failed) {
      await _replace(_items[index]._waitingAgain());
    }
    _kick();
  }

  Future<void> _deleteThumbnail(QueuedDocument item) async {
    final thumbnail = thumbnailOf(item);
    try {
      if (thumbnail != null && await thumbnail.exists()) {
        await thumbnail.delete();
      }
    } on FileSystemException {
      // The next start deletes it.
    }
  }

  /// Starts uploading. Call once, when the phone has a device key.
  void start() {
    _started = true;
    _kick();
  }

  /// Holds the queue: nothing new uploads (a running upload finishes and its result is saved),
  /// a retry wait ends at once. [enqueue] still works, so a scan taken now is kept.
  void pause(QueueHold reason) {
    _hold.value = reason;
    final wake = _wake;
    _wake = null;
    wake?.complete();
  }

  /// Clears a hold and uploads again.
  void resume() {
    _hold.value = null;
    _kick();
  }

  /// Ends the current wait and tries the next upload now.
  void retryNow() => _kick();

  /// Stops the queue: nothing new starts, a running upload finishes and its result is saved,
  /// then the queue saves nothing more.
  Future<void> close() async {
    _closed = true;
    final wake = _wake;
    _wake = null;
    wake?.complete();

    await _loop;
    try {
      await _saveChain;
    } on Object {
      // A failed save was already reported to its caller.
    }
    await _changes.close();
  }

  void _kick() {
    if (!_started || _closed || _hold.value != null) {
      return;
    }

    if (_loop != null) {
      // The loop is running. If it waits for a retry, wake it.
      final wake = _wake;
      _wake = null;
      wake?.complete();
      return;
    }

    _loop = _run().whenComplete(() => _loop = null);
  }

  Future<void> _run() async {
    while (!_closed && _hold.value == null) {
      final index = _items.indexWhere((item) => item.state == QueuedDocument.waiting);
      if (index < 0) {
        return;
      }

      final item = _items[index].copyWith(state: QueuedDocument.uploading);
      await _replace(item);

      UploadOutcome outcome;
      try {
        outcome = await _uploader.upload(
          pdf: pdfOf(item),
          clientDocumentId: item.clientDocumentId,
          documentType: item.documentType,
          pageCount: item.pageCount,
          staffId: item.staffId,
          title: item.title,
          note: item.userNote,
          thumbnail: thumbnailOf(item),
        );
      } on Object catch (error) {
        // Never lose an item to an unexpected error: try it again later.
        outcome = UploadRetryLater('The upload stopped with an error: $error');
      }

      switch (outcome) {
        case UploadAccepted(:final documentId):
          // Save the receipt first; the PDF goes only once the receipt is on disk.
          if (await _replace(item.copyWith(state: QueuedDocument.received, documentId: documentId))) {
            try {
              await pdfOf(item).delete();
            } on FileSystemException {
              // Already gone.
            }
          }
        case UploadRejected(:final reason):
          await _replace(item.copyWith(state: QueuedDocument.failed, attempts: item.attempts + 1, lastError: reason));
        case UploadDeviceRevoked():
          // Set the hold first, so the loop stops before anything else starts; then save.
          _hold.value = QueueHold.deviceRevoked;
          await _replace(item.copyWith(state: QueuedDocument.waiting));
        case UploadRetryLater(:final reason):
          final attempts = item.attempts + 1;
          // Behind the other waiting items, so one bad item does not block the rest.
          _items.removeWhere((existing) => existing.clientDocumentId == item.clientDocumentId);
          _items.add(item.copyWith(state: QueuedDocument.waiting, attempts: attempts, lastError: reason));
          await _saveQuietly();
          if (!_closed) {
            await _wait(_backoff(attempts));
          }
      }
    }
  }

  Future<void> _wait(Duration duration) async {
    final wake = Completer<void>();
    _wake = wake;
    await Future.any([_delay(duration), wake.future]);
    if (identical(_wake, wake)) {
      _wake = null;
    }
  }

  /// Replaces the item and saves; returns false when the save failed (the item stays changed in
  /// memory and the next save writes it). The upload loop never stops for a failed save.
  Future<bool> _replace(QueuedDocument item) async {
    final index = _items.indexWhere((existing) => existing.clientDocumentId == item.clientDocumentId);
    if (index < 0) {
      return false;
    }
    _items[index] = item;
    return _saveQuietly();
  }

  Future<bool> _saveQuietly() async {
    try {
      await _save();
      return true;
    } on Object {
      return false;
    }
  }

  List<QueuedDocument> _newestFirst() {
    final indexed = [for (var i = 0; i < _items.length; i++) (i, _items[i])];
    indexed.sort((a, b) {
      final byTime = b.$2.createdAt.compareTo(a.$2.createdAt);
      return byTime != 0 ? byTime : b.$1.compareTo(a.$1);
    });
    return List.unmodifiable([for (final (_, item) in indexed) item]);
  }

  void _emit() {
    if (!_changes.isClosed) {
      _changes.add(_newestFirst());
    }
  }

  /// Saves a snapshot of the items. Saves run one after the other, in call order.
  Future<void> _save() {
    final json = jsonEncode([for (final item in _items) item.toJson()]);
    _emit();
    final write = _saveChain.then((_) => _write(json));
    _saveChain = write.then((_) {}, onError: (Object _) {});
    return write;
  }

  Future<void> _write(String json) async {
    final stateFile = File('${_directory.path}/$_stateFile');
    final temporary = File('${_directory.path}/$_stateFile.tmp');
    await temporary.writeAsString(json, flush: true);
    if (await stateFile.exists()) {
      await stateFile.copy('${_directory.path}/$_backupFile');
    }
    await temporary.rename(stateFile.path);
  }
}
