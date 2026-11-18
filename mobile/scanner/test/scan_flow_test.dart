import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/app.dart';
import 'package:scanner/device/device_session.dart';
import 'package:scanner/device/device_store.dart';
import 'package:scanner/device/setup_code.dart';
import 'package:scanner/queue/upload_queue.dart';
import 'package:scanner/scanner/document_scanner.dart';
import 'package:scanner/screens/review_screen.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

const _pod = DocumentType(code: 'POD', displayName: 'Proof of delivery');
const _general = DocumentType(code: 'GENERAL', displayName: 'General document');

class _Store implements DeviceStore {
  @override
  Future<DeviceCredentials?> read() async => DeviceCredentials(
        apiBaseUri: Uri.parse('https://api.example.test'),
        deviceKey: 'pvmd_key',
        deviceName: 'Dispatch phone 1',
      );

  @override
  Future<void> write(DeviceCredentials value) async {}

  @override
  Future<void> deleteKey() async {}

  @override
  Future<void> setDeviceName(String name) async {}
}

class _NoEnroller implements Enroller {
  @override
  Future<EnrolOutcome> enrol(SetupCode code) async => const EnrolRefused(EnrolMessages.invalidCode);
}

/// The configured types; every upload is accepted.
class _Client implements DeviceClient {
  _Client(this.types);

  final List<DocumentType> types;
  final uploads = <({String documentType, String? title, String? note})>[];

  @override
  Future<DeviceConfig> config() async =>
      DeviceConfig(deviceName: 'Dispatch phone 1', documentTypes: types, staff: const []);

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
    String? title,
    String? note,
    File? thumbnail,
  }) async {
    uploads.add((documentType: documentType, title: title, note: note));
    return UploadAccepted(documentId: 'doc-${uploads.length}', state: 'Received');
  }
}

/// Answers each scan with the next scripted document; after the script, the user closed it.
class _Scanner implements DocumentScanner {
  final script = <ScannedDocument?>[];
  var calls = 0;

  @override
  Future<ScannedDocument?> scan() async {
    calls++;
    return script.isEmpty ? null : script.removeAt(0);
  }
}

void main() {
  late Directory root;
  late _Scanner scanner;
  late DeviceSession session;
  late _Client client;

  setUp(() {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();
    root = Directory.systemTemp.createTempSync('scan_flow_test');
    scanner = _Scanner();
  });

  tearDown(() => root.deleteSync(recursive: true));

  // Real file IO runs outside the fake clock: give it time, then let the widgets catch up.
  Future<void> settle(WidgetTester tester, [bool Function()? done]) async {
    for (var i = 0; i < 300 && !(done?.call() ?? false); i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 10)));
      await tester.pump(const Duration(milliseconds: 20));
      if (done == null && i >= 30) {
        break;
      }
    }
    await tester.pump();
  }

  Future<ScannedDocument> document(WidgetTester tester, String name, {int pages = 1}) async {
    final pdf = File('${root.path}/$name.pdf');
    await tester.runAsync(() => pdf.writeAsBytes('%PDF-1.7 $name'.codeUnits));
    return ScannedDocument(pdf: pdf, pageCount: pages);
  }

  Future<void> startApp(WidgetTester tester, List<DocumentType> types) async {
    client = _Client(types);
    final settings = AppSettings(SharedPreferencesAsync());
    session = DeviceSession(
      store: _Store(),
      enroller: _NoEnroller(),
      settings: settings,
      queueDirectory: () async => Directory('${root.path}/upload_queue'),
      clientFor: (_) => client,
      delay: (_) async {},
    );
    await tester.pumpWidget(ScannerApp(
      services: AppServices(session: session, scanner: scanner, settings: settings),
    ));
    await settle(tester, () => session.phase == SessionPhase.ready && session.config != null);
    await settle(tester);
  }

  Future<void> closeApp(WidgetTester tester) async {
    var closed = false;
    unawaited(session.close().then((_) => closed = true));
    await settle(tester, () => closed);
  }

  List<QueuedDocument> queued() => session.queue!.current;

  // Scrolls the screen on top until [finder] is on screen.
  Future<void> reveal(WidgetTester tester, Finder finder) async {
    await tester.scrollUntilVisible(finder, 80, scrollable: find.byType(Scrollable).last);
    await tester.pump();
  }

  testWidgets('the_start_screen_shows_the_configured_types_and_the_empty_state', (tester) async {
    await startApp(tester, const [_pod, _general]);

    expect(find.text('Proof of delivery'), findsOneWidget);
    expect(find.text('General document'), findsOneWidget);
    expect(find.text('No scans yet. Choose a document type to start.'), findsOneWidget);
    expect(find.byTooltip('Settings'), findsOneWidget);
    // Two types: the person chooses; the camera does not open by itself.
    expect(scanner.calls, 0);
    await closeApp(tester);
  });

  testWidgets('with_only_one_type_the_start_screen_has_no_other_type_and_the_scanner_opens_at_once',
      (tester) async {
    await startApp(tester, const [_pod]);

    expect(find.text('General document'), findsNothing);
    expect(find.text('Proof of delivery'), findsOneWidget);
    expect(scanner.calls, 1);
    await closeApp(tester);
  });

  testWidgets('send_queues_the_scan_with_its_fields_and_opens_the_scanner_again', (tester) async {
    await startApp(tester, const [_pod, _general]);
    scanner.script.add(await document(tester, 'lease', pages: 3));

    await tester.tap(find.text('General document'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);

    expect(find.text('3 pages'), findsOneWidget);
    expect(find.text('Title (optional)'), findsOneWidget);
    expect(find.text('Note (optional)'), findsOneWidget);
    await tester.enterText(find.widgetWithText(TextField, 'Title (optional)'), 'Lease unit 4');
    await tester.enterText(find.widgetWithText(TextField, 'Note (optional)'), 'Signed copy');
    await reveal(tester, find.text('Send'));
    await tester.tap(find.text('Send'));
    await settle(tester, () => scanner.calls == 2 && queued().isNotEmpty);
    await settle(tester);

    // The scanner opened again for the next paper; the person closed it.
    expect(scanner.calls, 2);
    expect(find.byType(ReviewScreen), findsNothing);
    final item = queued().single;
    expect(item.documentType, 'GENERAL');
    expect(item.title, 'Lease unit 4');
    expect(item.userNote, 'Signed copy');
    expect(find.text('Lease unit 4'), findsOneWidget);
    expect(find.text('No scans yet. Choose a document type to start.'), findsNothing);
    expect(FocusManager.instance.primaryFocus?.context?.widget, isNot(isA<EditableText>()));
    await closeApp(tester);
  });

  testWidgets('scan_again_discards_the_scan_and_finish_sends_and_goes_back_to_the_start', (tester) async {
    await startApp(tester, const [_pod, _general]);
    final first = await document(tester, 'first');
    scanner.script
      ..add(first)
      ..add(await document(tester, 'second', pages: 2));

    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);
    // Proof of delivery has a note, no title.
    expect(find.text('Title (optional)'), findsNothing);
    expect(find.text('Note (optional)'), findsOneWidget);
    expect(find.text('1 page'), findsOneWidget);

    await reveal(tester, find.text('Scan again'));
    await tester.tap(find.text('Scan again'));
    await settle(tester, () => find.text('2 pages').evaluate().isNotEmpty);
    await settle(tester); // The first review screen's exit animation.
    expect(scanner.calls, 2);
    expect(await tester.runAsync(first.pdf.exists), isFalse);

    await reveal(tester, find.text('Send and finish'));
    await tester.tap(find.text('Send and finish'));
    await settle(tester, () => queued().isNotEmpty && queued().single.state == QueuedDocument.received);
    await settle(tester);

    // Back on the start screen, without opening the scanner again.
    expect(scanner.calls, 2);
    expect(find.byType(ReviewScreen), findsNothing);
    expect(find.text('General document'), findsOneWidget);
    final item = queued().single;
    expect(item.documentType, 'POD');
    expect(item.pageCount, 2);
    expect(item.title, isNull);
    expect(client.uploads.single.title, isNull);
    expect(find.text('Sent'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('closing_the_scanner_without_a_scan_goes_back_to_the_start_screen', (tester) async {
    await startApp(tester, const [_pod, _general]);

    await tester.tap(find.text('General document'));
    await settle(tester, () => scanner.calls == 1);
    await settle(tester);

    expect(find.byType(ReviewScreen), findsNothing);
    expect(queued(), isEmpty);
    expect(find.text('No scans yet. Choose a document type to start.'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('small_phone_with_large_text_has_no_overflow_on_the_start_and_review_screens', (tester) async {
    tester.view.physicalSize = const Size(320, 568);
    tester.view.devicePixelRatio = 1;
    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.view.reset);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);

    await startApp(tester, const [_pod, _general]);
    expect(tester.takeException(), isNull);

    scanner.script.add(await document(tester, 'big'));
    await reveal(tester, find.text('General document'));
    await tester.tap(find.text('General document'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);
    expect(tester.takeException(), isNull);

    await reveal(tester, find.text('Send and finish'));
    await tester.tap(find.text('Send and finish'));
    await settle(tester, () => queued().isNotEmpty);
    await settle(tester);
    expect(tester.takeException(), isNull);
    await reveal(tester, find.text('Untitled'));
    expect(find.text('Untitled'), findsOneWidget);
    await closeApp(tester);
  });
}
