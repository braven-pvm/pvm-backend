import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/device/device_session.dart';
import 'package:scanner/device/device_store.dart';
import 'package:scanner/device/setup_code.dart';
import 'package:scanner/queue/upload_queue.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

class MemoryDeviceStore implements DeviceStore {
  MemoryDeviceStore([this.credentials]);

  DeviceCredentials? credentials;

  /// What [deleteKey] leaves: the address and name, for the report in a test.
  ({Uri apiBaseUri, String deviceName})? leftAfterDelete;

  @override
  Future<DeviceCredentials?> read() async => credentials;

  @override
  Future<void> write(DeviceCredentials value) async => credentials = value;

  @override
  Future<void> deleteKey() async {
    final current = credentials;
    if (current != null) {
      leftAfterDelete = (apiBaseUri: current.apiBaseUri, deviceName: current.deviceName);
    }
    credentials = null;
  }

  @override
  Future<void> setDeviceName(String name) async {
    final current = credentials;
    if (current != null) {
      credentials = DeviceCredentials(apiBaseUri: current.apiBaseUri, deviceKey: current.deviceKey, deviceName: name);
    }
  }
}

class FakeEnroller implements Enroller {
  FakeEnroller(this.outcome);

  EnrolOutcome outcome;
  final List<SetupCode> calls = [];

  @override
  Future<EnrolOutcome> enrol(SetupCode code) async {
    calls.add(code);
    return outcome;
  }
}

class FakeDeviceClient implements DeviceClient {
  FakeDeviceClient({required this.credentials, required this.outcomes, this.configResult});

  final DeviceCredentials credentials;
  final List<UploadOutcome> outcomes;

  /// A [DeviceConfig] to return, or an [Exception] to throw; null: offline.
  Object? configResult;
  final List<({String clientDocumentId, String? staffId})> uploads = [];
  var configCalls = 0;

  @override
  Future<DeviceConfig> config() async {
    configCalls++;
    final result = configResult;
    if (result is DeviceConfig) {
      return result;
    }
    if (result is Exception) {
      throw result;
    }
    throw const DeviceApiException('offline');
  }

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  }) async {
    uploads.add((clientDocumentId: clientDocumentId, staffId: staffId));
    final index = uploads.length - 1;
    return index < outcomes.length ? outcomes[index] : outcomes.last;
  }
}

void main() {
  late Directory root;
  late Directory queueDirectory;
  late MemoryDeviceStore store;
  late AppSettings settings;
  final clients = <FakeDeviceClient>[];
  late List<UploadOutcome> Function() nextOutcomes;
  Object? nextConfig;

  final enrolled = DeviceCredentials(
    apiBaseUri: Uri.parse('https://api.example.test'),
    deviceKey: 'pvmd_old',
    deviceName: 'Dispatch phone 1',
  );
  const config = DeviceConfig(
    deviceName: 'Dispatch phone 1',
    documentTypes: [DocumentType(code: 'POD', displayName: 'Proof of delivery')],
    staff: [StaffMember(id: 's-1', name: 'Anna')],
  );

  setUp(() {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();
    settings = AppSettings(SharedPreferencesAsync());
    root = Directory.systemTemp.createTempSync('device_session_test');
    queueDirectory = Directory('${root.path}/upload_queue');
    store = MemoryDeviceStore();
    clients.clear();
    nextOutcomes = () => [const UploadAccepted(documentId: 'd', state: 'Received')];
    nextConfig = config;
  });

  tearDown(() => root.deleteSync(recursive: true));

  DeviceSession session(Enroller enroller) => DeviceSession(
        store: store,
        enroller: enroller,
        settings: settings,
        queueDirectory: () async => queueDirectory,
        clientFor: (credentials) {
          final client = FakeDeviceClient(credentials: credentials, outcomes: nextOutcomes(), configResult: nextConfig);
          clients.add(client);
          return client;
        },
        delay: (_) async {},
      );

  File scannedPdf([String name = 'scan.pdf']) =>
      File('${root.path}/$name')..writeAsBytesSync('%PDF-1.7 test'.codeUnits);

  /// The items in the queue file on disk.
  List<Map<String, dynamic>> queueFile() =>
      (jsonDecode(File('${queueDirectory.path}/queue.json').readAsStringSync()) as List<dynamic>)
          .cast<Map<String, dynamic>>();

  test('without_a_stored_key_the_phone_needs_setup', () async {
    final subject = session(FakeEnroller(const EnrolRefused('x')));

    await subject.start();

    expect(subject.phase, SessionPhase.needsSetup);
    expect(subject.setupMessage, isNull);
    expect(subject.queue, isNull);
    expect(clients, isEmpty);
  });

  test('a_stored_key_opens_the_queue_and_loads_the_config', () async {
    store.credentials = enrolled;
    final subject = session(FakeEnroller(const EnrolRefused('x')));

    await subject.start();
    await subject.refreshConfig();

    expect(subject.phase, SessionPhase.ready);
    expect(subject.queue, isNotNull);
    expect(clients.single.credentials.deviceKey, 'pvmd_old');
    expect(subject.config!.staff.single.name, 'Anna');
    await subject.close();
  });

  test('enrol_stores_the_key_address_and_name_then_opens_the_queue', () async {
    final enroller = FakeEnroller(const EnrolSucceeded(deviceKey: 'pvmd_new', deviceName: 'Dispatch phone 2'));
    final subject = session(enroller);
    await subject.start();

    final outcome = await subject.enrol(SetupCode(api: Uri.parse('https://qa.example.test'), code: 'c-1'));

    expect(outcome, isA<EnrolSucceeded>());
    expect(store.credentials!.deviceKey, 'pvmd_new');
    expect(store.credentials!.apiBaseUri.toString(), 'https://qa.example.test');
    expect(store.credentials!.deviceName, 'Dispatch phone 2');
    expect(subject.phase, SessionPhase.ready);
    expect(subject.setupMessage, isNull);
    expect(clients.single.credentials.deviceKey, 'pvmd_new');
    await subject.close();
  });

  test('a_refused_enrolment_stores_nothing_and_stays_on_setup', () async {
    final subject = session(FakeEnroller(const EnrolRefused(EnrolMessages.invalidCode)));
    await subject.start();

    final outcome = await subject.enrol(SetupCode(api: Uri.parse('https://qa.example.test'), code: 'c-1'));

    expect((outcome as EnrolRefused).message, EnrolMessages.invalidCode);
    expect(store.credentials, isNull);
    expect(subject.phase, SessionPhase.needsSetup);
    expect(clients, isEmpty);
  });

  test('a_401_on_upload_deletes_the_key_keeps_the_queue_and_pdfs_and_stops_the_loop', () async {
    store.credentials = enrolled;
    nextOutcomes = () => [const UploadDeviceRevoked()];
    final enroller = FakeEnroller(const EnrolSucceeded(deviceKey: 'pvmd_new', deviceName: 'Dispatch phone 1'));
    final subject = session(enroller);
    await subject.start();
    final queue = subject.queue!;

    final first = await queue.enqueue(scannedPdf('a.pdf'), 'POD', 1, staffId: 's-1');
    final second = await queue.enqueue(scannedPdf('b.pdf'), 'POD', 1);
    await queue.whenIdle;
    await pumpEventQueue();

    // The key is gone; the address stays for the record, the setup screen shows the message.
    expect(store.credentials, isNull);
    expect(store.leftAfterDelete!.apiBaseUri, enrolled.apiBaseUri);
    expect(subject.phase, SessionPhase.needsSetup);
    expect(subject.setupMessage, 'This phone was removed. Ask an admin for a new setup code.');
    expect(subject.queue, isNull);
    // The loop stopped after the first 401.
    expect(clients.single.uploads, hasLength(1));
    // The queue file and both PDFs are kept, with their client document IDs.
    final saved = queueFile();
    expect(saved.map((item) => item['clientDocumentId']), containsAll([first.clientDocumentId, second.clientDocumentId]));
    expect(saved.map((item) => item['state']), everyElement(QueuedDocument.waiting));
    expect(File('${queueDirectory.path}/pdfs/${first.clientDocumentId}.pdf').existsSync(), isTrue);
    expect(File('${queueDirectory.path}/pdfs/${second.clientDocumentId}.pdf').existsSync(), isTrue);

    // After a new setup code the same documents upload with the same IDs and the staff ID.
    nextOutcomes = () => [const UploadAccepted(documentId: 'd', state: 'Received')];
    await subject.enrol(SetupCode(api: Uri.parse('https://api.example.test'), code: 'c-2'));
    await subject.queue!.whenIdle;

    final again = clients.last.uploads;
    expect(clients.last.credentials.deviceKey, 'pvmd_new');
    expect(again.map((upload) => upload.clientDocumentId), [first.clientDocumentId, second.clientDocumentId]);
    expect(again.first.staffId, 's-1');
    expect(again.last.staffId, isNull);
    await subject.close();
  });

  test('a_401_on_config_also_deletes_the_key', () async {
    store.credentials = enrolled;
    nextConfig = const DeviceRevokedException();
    final subject = session(FakeEnroller(const EnrolRefused('x')));
    await subject.start();

    await subject.refreshConfig();

    expect(store.credentials, isNull);
    expect(subject.phase, SessionPhase.needsSetup);
    expect(subject.setupMessage, DeviceSession.removedMessage);
    await subject.close();
  });

  test('reset_deletes_the_key_only_and_shows_setup_without_a_message', () async {
    store.credentials = enrolled;
    nextOutcomes = () => [const UploadRetryLater('offline')];
    final subject = session(FakeEnroller(const EnrolRefused('x')));
    await subject.start();
    final queued = await subject.queue!.enqueue(scannedPdf(), 'POD', 1);
    await settings.setStaffId('s-1');

    await subject.reset();

    expect(store.credentials, isNull);
    expect(subject.phase, SessionPhase.needsSetup);
    expect(subject.setupMessage, isNull);
    expect(queueFile().single['clientDocumentId'], queued.clientDocumentId);
    expect(File('${queueDirectory.path}/pdfs/${queued.clientDocumentId}.pdf').existsSync(), isTrue);
    await subject.close();
  });

  test('after_a_revoke_or_reset_the_held_queue_still_takes_a_scan_and_sends_nothing', () async {
    for (final revoke in [true, false]) {
      clients.clear();
      store.credentials = enrolled;
      nextConfig = revoke ? const DeviceRevokedException() : config;
      nextOutcomes = () => [const UploadAccepted(documentId: 'd', state: 'Received')];
      final subject = session(FakeEnroller(const EnrolSucceeded(deviceKey: 'pvmd_new', deviceName: 'x')));
      await subject.start();
      final held = subject.queue!;

      if (revoke) {
        await subject.refreshConfig();
      } else {
        await subject.reset();
      }
      final late = await held.enqueue(scannedPdf('late-$revoke.pdf'), 'POD', 1, staffId: 's-1');
      await held.whenIdle;

      expect(subject.phase, SessionPhase.needsSetup, reason: 'revoke: $revoke');
      expect(clients.single.uploads, isEmpty, reason: 'revoke: $revoke');
      expect(queueFile().map((item) => item['clientDocumentId']), contains(late.clientDocumentId));

      // The next enrolment closes the held queue and sends the scan with the new key.
      await subject.enrol(SetupCode(api: Uri.parse('https://api.example.test'), code: 'c'));
      await subject.queue!.whenIdle;
      expect(clients.last.uploads.map((upload) => upload.clientDocumentId), contains(late.clientDocumentId));
      expect(clients.last.uploads.single.staffId, 's-1');
      await subject.close();
      queueDirectory.deleteSync(recursive: true);
    }
  });

  test('offline_at_start_the_last_config_is_used', () async {
    store.credentials = enrolled;
    final online = session(FakeEnroller(const EnrolRefused('x')));
    await online.start();
    await online.refreshConfig();
    await online.close();

    nextConfig = null; // Offline.
    final offline = session(FakeEnroller(const EnrolRefused('x')));
    await offline.start();
    await offline.refreshConfig();

    expect(offline.phase, SessionPhase.ready);
    expect(offline.config!.staff.single.id, 's-1');
    await offline.close();
  });
}
