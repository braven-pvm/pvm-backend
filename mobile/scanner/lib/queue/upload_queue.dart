import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../api/documents_api.dart';

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
  });

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

  QueuedDocument copyWith({String? state, int? attempts, String? lastError, String? documentId}) =>
      QueuedDocument(
        clientDocumentId: clientDocumentId,
        documentType: documentType,
        pageCount: pageCount,
        state: state ?? this.state,
        createdAt: createdAt,
        attempts: attempts ?? this.attempts,
        lastError: lastError ?? this.lastError,
        documentId: documentId ?? this.documentId,
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
      );
}

/// Why the queue stopped uploading until the user acts.
enum QueueHold {
  /// The token could not be renewed silently: the user must sign in again.
  signInRequired,

  /// The backend answered 403: the user may not upload documents.
  accessDenied,
}

/// Default retry delay: 5 s, 10 s, 20 s, ... up to 5 minutes.
Duration defaultBackoff(int attempt) {
  final seconds = 5 * math.pow(2, math.max(0, attempt - 1)).toInt();
  return Duration(seconds: math.min(seconds, 300));
}

/// A persistent upload queue.
///
/// Each PDF is copied into `directory/pdfs/<clientDocumentId>.pdf` and the item list is kept in
/// `directory/queue.json`, so nothing is lost when the app stops. Items upload one at a time,
/// oldest first. A failed attempt that may succeed later is retried with backoff and the same
/// client document ID. The local PDF is deleted only after the backend confirms receipt. A
/// rejected upload (400 or 413) is marked [QueuedDocument.failed] and keeps its PDF.
class UploadQueue {
  UploadQueue._(this._directory, this._uploader, this._backoff, this._delay, this._items);

  static const _stateFile = 'queue.json';
  static const _pdfFolder = 'pdfs';

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

  /// Opens the queue in [directory]. Items left in the uploading state by a stopped app are
  /// waiting again; received items from an earlier run are dropped.
  static Future<UploadQueue> open({
    required Directory directory,
    required DocumentUploader uploader,
    Duration Function(int attempt) backoff = defaultBackoff,
    Future<void> Function(Duration)? delay,
  }) async {
    await Directory('${directory.path}/$_pdfFolder').create(recursive: true);
    final items = <QueuedDocument>[];
    final stateFile = File('${directory.path}/$_stateFile');
    if (await stateFile.exists()) {
      final list = jsonDecode(await stateFile.readAsString()) as List<dynamic>;
      for (final entry in list.cast<Map<String, dynamic>>()) {
        final item = QueuedDocument.fromJson(entry);
        if (item.state == QueuedDocument.received) {
          continue;
        }
        items.add(item.state == QueuedDocument.uploading ? item.copyWith(state: QueuedDocument.waiting) : item);
      }
    }

    final queue = UploadQueue._(directory, uploader, backoff, delay ?? Future<void>.delayed, items);
    await queue._save();
    return queue;
  }

  /// The items, newest first. A new listener gets the current list at once.
  Stream<List<QueuedDocument>> get items async* {
    yield _newestFirst();
    yield* _changes.stream;
  }

  /// The items in the order they upload (oldest first).
  List<QueuedDocument> get current => List.unmodifiable(_items);

  /// Set while the queue waits for the user (sign in again, or no upload permission).
  ValueListenable<QueueHold?> get hold => _hold;

  /// Completes when the queue has nothing it can upload now (empty, held, or closed).
  Future<void> get whenIdle => _loop ?? Future<void>.value();

  /// The local PDF of [item]. It exists until the backend confirms receipt.
  File pdfOf(QueuedDocument item) => File('${_directory.path}/$_pdfFolder/${item.clientDocumentId}.pdf');

  /// Adds a scanned PDF. The file is moved into the queue folder.
  Future<QueuedDocument> enqueue(File pdf, String documentType, int pageCount) async {
    final item = QueuedDocument(
      clientDocumentId: const Uuid().v4(),
      documentType: documentType,
      pageCount: pageCount,
      state: QueuedDocument.waiting,
      createdAt: DateTime.now(),
    );

    await pdf.copy(pdfOf(item).path);
    try {
      await pdf.delete();
    } on FileSystemException {
      // The scanner's own copy is in the cache folder; Android clears it later.
    }

    _items.add(item);
    await _save();
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
    final pdf = pdfOf(item);
    if (await pdf.exists()) {
      await pdf.delete();
    }
    await _save();
  }

  /// Starts uploading. Call once, after sign-in.
  void start() {
    _started = true;
    _kick();
  }

  /// Clears a hold (after the user signed in again) and uploads again.
  void resume() {
    _hold.value = null;
    _kick();
  }

  /// Ends the current wait and tries the next upload now.
  void retryNow() => _kick();

  /// Stops the queue. A running upload finishes; nothing new starts.
  Future<void> close() async {
    _closed = true;
    _wake?.complete();
    _wake = null;
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
        );
      } on Object catch (error) {
        // Never lose an item to an unexpected error: try it again later.
        outcome = UploadRetryLater('The upload stopped with an error: $error');
      }

      switch (outcome) {
        case UploadAccepted(:final documentId):
          final pdf = pdfOf(item);
          if (await pdf.exists()) {
            await pdf.delete();
          }
          await _replace(item.copyWith(state: QueuedDocument.received, documentId: documentId));
        case UploadRejected(:final reason):
          await _replace(item.copyWith(state: QueuedDocument.failed, attempts: item.attempts + 1, lastError: reason));
        case UploadSignInRequired():
          await _replace(item.copyWith(state: QueuedDocument.waiting));
          _hold.value = QueueHold.signInRequired;
        case UploadAccessDenied():
          await _replace(item.copyWith(state: QueuedDocument.waiting));
          _hold.value = QueueHold.accessDenied;
        case UploadRetryLater(:final reason):
          final attempts = item.attempts + 1;
          await _replace(item.copyWith(state: QueuedDocument.waiting, attempts: attempts, lastError: reason));
          await _wait(_backoff(attempts));
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

  Future<void> _replace(QueuedDocument item) async {
    final index = _items.indexWhere((existing) => existing.clientDocumentId == item.clientDocumentId);
    if (index >= 0) {
      _items[index] = item;
      await _save();
    }
  }

  List<QueuedDocument> _newestFirst() => List.unmodifiable(_items.reversed);

  Future<void> _save() async {
    final stateFile = File('${_directory.path}/$_stateFile');
    final temporary = File('${stateFile.path}.tmp');
    await temporary.writeAsString(jsonEncode([for (final item in _items) item.toJson()]), flush: true);
    await temporary.rename(stateFile.path);
    if (!_changes.isClosed) {
      _changes.add(_newestFirst());
    }
  }
}
