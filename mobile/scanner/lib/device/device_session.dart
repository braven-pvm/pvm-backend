import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';

import '../api/device_api.dart';
import '../queue/upload_queue.dart';
import '../settings/app_settings.dart';
import 'device_store.dart';
import 'setup_code.dart';

enum SessionPhase { starting, needsSetup, ready, startFailed }

/// The phone's enrolment state (design 6.10): no key means the setup screen; a key means the
/// upload queue runs with it. A 401 from any device call deletes the key and goes back to the
/// setup screen; the queue file and the PDFs stay, and upload after the next enrolment with
/// the same client document IDs.
class DeviceSession extends ChangeNotifier {
  DeviceSession({
    required this._store,
    required this._enroller,
    required this._settings,
    required this._queueDirectory,
    required this._clientFor,
    this._backoff = defaultBackoff,
    this._delay,
  });

  static const removedMessage = 'This phone was removed. Ask an admin for a new setup code.';

  final DeviceStore _store;
  final Enroller _enroller;
  final AppSettings _settings;
  final Future<Directory> Function() _queueDirectory;
  final DeviceClient Function(DeviceCredentials credentials) _clientFor;
  final Duration Function(int attempt) _backoff;
  final Future<void> Function(Duration)? _delay;

  var _phase = SessionPhase.starting;
  String? _setupMessage;
  String? _startError;
  DeviceCredentials? _credentials;
  DeviceClient? _client;
  UploadQueue? _queue;
  DeviceConfig? _config;
  Future<void> _closing = Future<void>.value();

  SessionPhase get phase => _phase;

  /// Shown on the setup screen, for example [removedMessage]; null for a plain first start.
  String? get setupMessage => _setupMessage;

  /// Why the start failed ([SessionPhase.startFailed]).
  String? get startError => _startError;

  DeviceCredentials? get credentials => _credentials;
  UploadQueue? get queue => _queue;

  /// The last config from the server (or from the last run), or null before the first one.
  DeviceConfig? get config => _config;

  /// Reads the stored key and opens the queue, or shows the setup screen.
  Future<void> start() async {
    _set(SessionPhase.starting);
    try {
      final credentials = await _store.read();
      if (credentials == null) {
        _set(SessionPhase.needsSetup);
        return;
      }
      await _open(credentials);
    } on Object catch (error) {
      _startError = '$error';
      _set(SessionPhase.startFailed);
    }
  }

  /// Exchanges [code] for a device key; on success stores it and opens the queue.
  Future<EnrolOutcome> enrol(SetupCode code) async {
    final outcome = await _enroller.enrol(code);
    if (outcome is EnrolSucceeded) {
      final credentials = DeviceCredentials(
        apiBaseUri: code.api,
        deviceKey: outcome.deviceKey,
        deviceName: outcome.deviceName,
      );
      await _store.write(credentials);
      // The new server may have other names and types; the staff choice is checked against them.
      await _settings.setCachedConfig(null);
      _config = null;
      _setupMessage = null;
      try {
        await _open(credentials);
      } on Object catch (error) {
        _startError = '$error';
        _set(SessionPhase.startFailed);
      }
    }
    return outcome;
  }

  /// "Reset this phone": deletes the key only. Scans not yet sent stay on the phone.
  Future<void> reset() async {
    _client = null;
    _credentials = null;
    await _store.deleteKey();
    _setupMessage = null;
    _set(SessionPhase.needsSetup);
    await _closeQueue();
  }

  /// Loads the config from the server. Offline, the last known config stays. A 401 sends the
  /// phone back to the setup screen.
  Future<DeviceConfig?> refreshConfig() async {
    final client = _client;
    if (client == null) {
      return _config;
    }
    try {
      final config = await client.config();
      if (!identical(client, _client)) {
        return _config;
      }
      _config = config;
      await _settings.setCachedConfig(config);
      final credentials = _credentials;
      if (credentials != null && config.deviceName.isNotEmpty && config.deviceName != credentials.deviceName) {
        _credentials = DeviceCredentials(
          apiBaseUri: credentials.apiBaseUri,
          deviceKey: credentials.deviceKey,
          deviceName: config.deviceName,
        );
        await _store.setDeviceName(config.deviceName);
      }
      notifyListeners();
    } on DeviceRevokedException {
      if (identical(client, _client)) {
        await _revoked();
      }
    } on DeviceApiException {
      // Offline or a server problem: keep the last config.
    }
    return _config;
  }

  /// Stops the queue; for the end of the app or a test.
  Future<void> close() async {
    _client = null;
    await _closeQueue();
  }

  Future<void> _open(DeviceCredentials credentials) async {
    await _closing;
    final client = _clientFor(credentials);
    final queue = await UploadQueue.open(
      directory: await _queueDirectory(),
      uploader: client,
      backoff: _backoff,
      delay: _delay,
    );
    _credentials = credentials;
    _client = client;
    _queue = queue;
    _config ??= await _settings.cachedConfig();
    queue.hold.addListener(_onHold);
    queue.start();
    _set(SessionPhase.ready);
  }

  void _onHold() {
    if (_queue?.hold.value == QueueHold.deviceRevoked) {
      unawaited(_revoked());
    }
  }

  Future<void> _revoked() async {
    if (_client == null) {
      return; // Already handled.
    }
    _client = null;
    _credentials = null;
    await _store.deleteKey();
    _setupMessage = removedMessage;
    _set(SessionPhase.needsSetup);
    await _closeQueue();
  }

  Future<void> _closeQueue() {
    final queue = _queue;
    _queue = null;
    if (queue == null) {
      return _closing;
    }
    queue.hold.removeListener(_onHold);
    return _closing = queue.close().catchError((Object _) {});
  }

  void _set(SessionPhase phase) {
    _phase = phase;
    notifyListeners();
  }
}
