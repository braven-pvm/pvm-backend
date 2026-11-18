import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/app.dart';
import 'package:scanner/device/device_session.dart';
import 'package:scanner/device/device_store.dart';
import 'package:scanner/device/setup_code.dart';
import 'package:scanner/queue/upload_queue.dart';
import 'package:scanner/scanner/document_scanner.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

class _Store implements DeviceStore {
  DeviceCredentials? credentials = DeviceCredentials(
    apiBaseUri: Uri.parse('https://api.example.test'),
    deviceKey: 'pvmd_key',
    deviceName: 'Dispatch phone 1',
  );

  @override
  Future<DeviceCredentials?> read() async => credentials;

  @override
  Future<void> write(DeviceCredentials value) async => credentials = value;

  @override
  Future<void> deleteKey() async => credentials = null;

  @override
  Future<void> setDeviceName(String name) async {}
}

class _NoEnroller implements Enroller {
  @override
  Future<EnrolOutcome> enrol(SetupCode code) async => const EnrolRefused(EnrolMessages.invalidCode);
}

/// Config works until [revoked]; then every call is a 401.
class _Client implements DeviceClient {
  var revoked = false;
  var uploads = 0;

  @override
  Future<DeviceConfig> config() async {
    if (revoked) {
      throw const DeviceRevokedException();
    }
    return const DeviceConfig(
      deviceName: 'Dispatch phone 1',
      documentTypes: [DocumentType(code: 'POD', displayName: 'Proof of delivery')],
      staff: [StaffMember(id: 's-1', name: 'Anna')],
    );
  }

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  }) async {
    uploads++;
    return const UploadDeviceRevoked();
  }
}

/// The first scan waits for [first]; later scans: the user closed the scanner.
class _Scanner implements DocumentScanner {
  final first = Completer<ScannedDocument?>();
  var calls = 0;

  @override
  Future<ScannedDocument?> scan() {
    calls++;
    return calls == 1 ? first.future : Future.value();
  }
}

void main() {
  testWidgets('a_scan_that_ends_after_a_401_is_kept_in_the_queue_with_its_type_and_staff', (tester) async {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();
    final root = Directory.systemTemp.createTempSync('revoke_during_scan_test');
    addTearDown(() => root.deleteSync(recursive: true));
    final queueDirectory = Directory('${root.path}/upload_queue');

    final settings = AppSettings(SharedPreferencesAsync());
    await tester.runAsync(() => settings.setStaffId('s-1'));
    final store = _Store();
    final client = _Client();
    final scanner = _Scanner();
    final session = DeviceSession(
      store: store,
      enroller: _NoEnroller(),
      settings: settings,
      queueDirectory: () async => queueDirectory,
      clientFor: (_) => client,
      delay: (_) async {},
    );

    // Real file IO runs outside the fake clock: give it time, then let the widgets catch up,
    // until [done] (or a few seconds).
    Future<void> settle(bool Function() done) async {
      for (var i = 0; i < 300 && !done(); i++) {
        await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 10)));
        await tester.pump(const Duration(milliseconds: 10));
      }
      await tester.pump();
    }

    // Polls the PDF folder, not queue.json: reading that file while the queue renames onto it
    // fails the rename on Windows.
    bool queueHasPdf() {
      final folder = Directory('${queueDirectory.path}/pdfs');
      return folder.existsSync() && folder.listSync().isNotEmpty;
    }

    await tester.pumpWidget(ScannerApp(
      services: AppServices(session: session, scanner: scanner, settings: settings),
    ));
    await settle(() => scanner.calls == 1);

    // The batch screen opened the camera; the person is scanning.
    expect(session.phase, SessionPhase.ready);
    expect(scanner.calls, 1);

    // The admin revokes the phone: the next device call gets 401.
    client.revoked = true;
    await tester.runAsync(session.refreshConfig);
    await settle(() => find.text('Scan setup code').evaluate().isNotEmpty);
    expect(session.phase, SessionPhase.needsSetup);
    expect(store.credentials, isNull);
    expect(find.text('Scan setup code'), findsOneWidget);

    // The scan the person was taking ends now.
    final pdf = File('${root.path}/scanner-cache.pdf');
    await tester.runAsync(() => pdf.writeAsBytes('%PDF-1.7 late scan'.codeUnits));
    scanner.first.complete(ScannedDocument(pdf: pdf, pageCount: 2));
    await settle(() => queueHasPdf() && !pdf.existsSync());

    final saved = await tester.runAsync(() async =>
        (jsonDecode(await File('${queueDirectory.path}/queue.json').readAsString()) as List<dynamic>)
            .cast<Map<String, dynamic>>());
    expect(saved, hasLength(1));
    final item = saved!.single;
    expect(item['state'], QueuedDocument.waiting);
    expect(item['documentType'], 'POD');
    expect(item['pageCount'], 2);
    expect(item['staffId'], 's-1');
    final kept = File('${queueDirectory.path}/pdfs/${item['clientDocumentId']}.pdf');
    expect(await tester.runAsync(kept.exists), isTrue);
    expect(await tester.runAsync(pdf.exists), isFalse);
    // Held: nothing was sent with the revoked key, and the camera did not open again.
    expect(client.uploads, 0);
    expect(scanner.calls, 1);

    // The queue's futures live in the test's fake zone: close it there, and let the IO run.
    var closed = false;
    unawaited(session.close().then((_) => closed = true));
    await settle(() => closed);
    expect(closed, isTrue);
  });
}
