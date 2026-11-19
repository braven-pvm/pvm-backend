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
import 'package:scanner/screens/home_screen.dart';
import 'package:scanner/screens/review_screen.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

const _pod = DocumentType(code: 'POD', displayName: 'Proof of delivery');
const _general = DocumentType(code: 'GENERAL', displayName: 'General document');
const _invoice = DocumentType(code: 'INVOICE', displayName: 'Supplier invoice');

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

/// The configured types; each upload answers with the next scripted outcome, then accepts.
class _Client implements DeviceClient {
  _Client(this.types);

  List<DocumentType> types;
  final outcomes = <UploadOutcome>[];
  final uploads = <({String documentType, String? title, String? note})>[];
  var configCalls = 0;

  @override
  Future<DeviceConfig> config() async {
    configCalls++;
    return DeviceConfig(deviceName: 'Dispatch phone 1', documentTypes: types, staff: const []);
  }

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
    if (outcomes.isNotEmpty) {
      return outcomes.removeAt(0);
    }
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
  late StreamController<void> network;
  var buzzes = 0;
  late DateTime now;

  setUp(() {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();
    root = Directory.systemTemp.createTempSync('scan_flow_test');
    scanner = _Scanner();
    network = StreamController<void>.broadcast();
    buzzes = 0;
    now = DateTime(2026, 11, 19, 9);
  });

  tearDown(() {
    network.close();
    root.deleteSync(recursive: true);
  });

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

  /// [retryWaitsForever]: the fake clock never ends a retry wait; only a wake does.
  Future<void> startApp(WidgetTester tester, List<DocumentType> types, {bool retryWaitsForever = false}) async {
    client = _Client(types);
    final settings = AppSettings(SharedPreferencesAsync());
    session = DeviceSession(
      store: _Store(),
      enroller: _NoEnroller(),
      settings: settings,
      queueDirectory: () async => Directory('${root.path}/upload_queue'),
      clientFor: (_) => client,
      delay: retryWaitsForever ? (_) => Completer<void>().future : (_) async {},
    );
    await tester.pumpWidget(
      ScannerApp(
        services: AppServices(
          session: session,
          scanner: scanner,
          settings: settings,
          networkChanges: network.stream,
          confirmQueued: () async => buzzes++,
          clock: () => now,
        ),
      ),
    );
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

  testWidgets('the_start_screen_shows_the_types_and_the_upload_status', (tester) async {
    await startApp(tester, const [_pod, _general]);

    expect(find.text('Proof of delivery'), findsOneWidget);
    expect(find.text('General document'), findsOneWidget);
    expect(find.text(HomeScreen.allSentText), findsOneWidget);
    expect(find.text('Sent today: 0'), findsOneWidget);
    expect(find.byTooltip('Settings'), findsOneWidget);
    // The phone's name heads the screen.
    expect(find.text('Dispatch phone 1'), findsOneWidget);
    // The first type is the main action; the others are outlined.
    expect(find.widgetWithText(FilledButton, 'Proof of delivery'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'General document'), findsOneWidget);
    expect(find.text(HomeScreen.hintOf('POD')!), findsOneWidget);
    // The type buttons sit below the status, at the bottom of the screen (thumb reach).
    final screen = tester.getSize(find.byType(HomeScreen));
    expect(tester.getTopLeft(find.text('Proof of delivery')).dy, greaterThan(screen.height / 2));
    expect(
      tester.getTopLeft(find.text(HomeScreen.allSentText)).dy,
      lessThan(tester.getTopLeft(find.text('Proof of delivery')).dy),
    );
    // Two types: the person chooses; the camera does not open by itself.
    expect(scanner.calls, 0);
    await closeApp(tester);
  });

  testWidgets('with_only_one_type_the_scanner_opens_at_once', (tester) async {
    await startApp(tester, const [_pod]);

    expect(find.text('General document'), findsNothing);
    expect(find.text('Proof of delivery'), findsOneWidget);
    expect(scanner.calls, 1);
    await closeApp(tester);
  });

  testWidgets('proof_of_delivery_queues_each_scan_without_a_review_and_reopens_the_scanner', (tester) async {
    await startApp(tester, const [_pod, _general]);
    scanner.script
      ..add(await document(tester, 'pod-1'))
      ..add(await document(tester, 'pod-2', pages: 2))
      ..add(await document(tester, 'pod-3'));

    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => scanner.calls == 4 && client.uploads.length == 3 && queued().isEmpty);
    await settle(tester);

    // Three scans, then the person closed the scanner: four openings, no review screen.
    expect(scanner.calls, 4);
    expect(find.byType(ReviewScreen), findsNothing);
    expect(client.uploads.map((upload) => upload.documentType), ['POD', 'POD', 'POD']);
    expect(client.uploads.every((upload) => upload.title == null && upload.note == null), isTrue);
    expect(buzzes, 3);
    // Sent and gone from the phone; only the counter is left.
    expect(queued(), isEmpty);
    expect(find.text('Sent today: 3'), findsOneWidget);
    expect(find.text('Proof of delivery'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('another_type_without_a_title_also_skips_the_review', (tester) async {
    await startApp(tester, const [_pod, _invoice]);
    scanner.script.add(await document(tester, 'invoice'));

    await tester.tap(find.text('Supplier invoice'));
    await settle(tester, () => scanner.calls == 2 && client.uploads.isNotEmpty);

    expect(find.byType(ReviewScreen), findsNothing);
    expect(client.uploads.single.documentType, 'INVOICE');
    await closeApp(tester);
  });

  testWidgets('general_next_queues_the_scan_with_title_and_note_and_reopens_the_scanner', (tester) async {
    await startApp(tester, const [_pod, _general]);
    scanner.script.add(await document(tester, 'lease', pages: 3));

    await tester.tap(find.text('General document'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);

    expect(find.text('3 pages'), findsOneWidget);
    expect(find.text('Title (optional)'), findsOneWidget);
    expect(find.text('Note (optional)'), findsOneWidget);
    // One button only.
    expect(find.text('Send'), findsNothing);
    expect(find.text('Send and finish'), findsNothing);
    expect(find.text('Scan again'), findsNothing);
    await tester.enterText(find.widgetWithText(TextField, 'Title (optional)'), 'Lease unit 4');
    await tester.enterText(find.widgetWithText(TextField, 'Note (optional)'), 'Signed copy');
    await reveal(tester, find.text(ReviewScreen.nextLabel));
    await tester.tap(find.text(ReviewScreen.nextLabel));
    await settle(tester, () => scanner.calls == 2 && client.uploads.isNotEmpty);
    await settle(tester);

    // The scanner opened again for the next paper; the person closed it.
    expect(scanner.calls, 2);
    expect(find.byType(ReviewScreen), findsNothing);
    final upload = client.uploads.single;
    expect(upload.documentType, 'GENERAL');
    expect(upload.title, 'Lease unit 4');
    expect(upload.note, 'Signed copy');
    expect(buzzes, 1);
    expect(FocusManager.instance.primaryFocus?.context?.widget, isNot(isA<EditableText>()));
    await closeApp(tester);
  });

  testWidgets('back_on_the_general_review_screen_still_queues_the_scan_and_stops', (tester) async {
    await startApp(tester, const [_pod, _general]);
    scanner.script
      ..add(await document(tester, 'first'))
      ..add(await document(tester, 'second'));

    await tester.tap(find.text('General document'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);
    await tester.enterText(find.widgetWithText(TextField, 'Title (optional)'), 'Kept anyway');

    await tester.tap(find.byType(BackButton));
    await settle(tester, () => client.uploads.isNotEmpty);
    await settle(tester);

    expect(find.byType(ReviewScreen), findsNothing);
    expect(scanner.calls, 1);
    expect(client.uploads.single.title, 'Kept anyway');
    expect(find.text('General document'), findsOneWidget);

    // The phone's own back key does the same.
    await tester.tap(find.text('General document'));
    await settle(tester, () => find.byType(ReviewScreen).evaluate().isNotEmpty);
    await tester.binding.handlePopRoute();
    await settle(tester, () => client.uploads.length == 2);
    await settle(tester);
    expect(find.byType(ReviewScreen), findsNothing);
    expect(scanner.calls, 2);
    await closeApp(tester);
  });

  testWidgets('closing_the_scanner_without_a_scan_goes_back_to_the_start_screen', (tester) async {
    await startApp(tester, const [_pod, _general]);

    await tester.tap(find.text('General document'));
    await settle(tester, () => scanner.calls == 1);
    await settle(tester);

    expect(find.byType(ReviewScreen), findsNothing);
    expect(queued(), isEmpty);
    expect(client.uploads, isEmpty);
    expect(find.text(HomeScreen.allSentText), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('a_network_error_shows_waiting_for_network_and_a_network_change_sends_at_once', (tester) async {
    await startApp(tester, const [_pod, _general], retryWaitsForever: true);
    client.outcomes.add(const UploadRetryLater('No connection to the server: Network is unreachable'));
    scanner.script.add(await document(tester, 'offline'));

    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => client.uploads.length == 1 && session.queue!.status.value == QueueStatus.retrying);
    await settle(tester);

    expect(find.text(HomeScreen.retryingText), findsOneWidget);
    expect(find.text('1 waiting'), findsOneWidget);
    // A transport problem has no buttons: the app tries again by itself.
    expect(find.text('Try again'), findsNothing);
    expect(find.text('Remove'), findsNothing);

    network.add(null); // Wi-Fi or mobile data came back.
    await settle(tester, () => client.uploads.length == 2 && queued().isEmpty);
    await settle(tester);

    expect(find.text(HomeScreen.retryingText), findsNothing);
    expect(find.text('Sent today: 1'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('coming_to_the_front_sends_at_once_and_loads_the_types_again', (tester) async {
    await startApp(tester, const [_pod, _invoice], retryWaitsForever: true);
    client.outcomes.add(const UploadRetryLater('The server had a problem (HTTP 503).'));
    scanner.script.add(await document(tester, 'later'));
    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => client.uploads.length == 1 && session.queue!.status.value == QueueStatus.retrying);
    expect(find.text('General document'), findsNothing);
    final configCalls = client.configCalls;

    // An admin adds a type while the app is in the background; the last load is 5 minutes old.
    client.types = const [_pod, _invoice, _general];
    now = now.add(const Duration(minutes: 5));
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await settle(tester, () => client.uploads.length == 2 && find.text('General document').evaluate().isNotEmpty);
    await settle(tester);

    expect(client.configCalls, greaterThan(configCalls));
    expect(find.text('General document'), findsOneWidget);
    expect(queued(), isEmpty);
    await closeApp(tester);
  });

  testWidgets('a_rejected_scan_is_a_problem_with_its_reason_and_remove_after_confirmation', (tester) async {
    await startApp(tester, const [_pod, _general]);
    client.outcomes.add(const UploadRejected('The server did not accept this document. The file is not a PDF.'));
    scanner.script.add(await document(tester, 'bad'));

    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => queued().isNotEmpty && queued().single.state == QueuedDocument.failed);
    await settle(tester);

    expect(find.text('Problem'), findsOneWidget);
    expect(find.text('The server did not accept this document. The file is not a PDF.'), findsOneWidget);
    await reveal(tester, find.text('Remove'));
    await tester.tap(find.text('Remove'));
    await tester.pumpAndSettle();
    expect(find.text('Remove this scan?'), findsOneWidget);
    await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Remove')));
    await settle(tester, () => queued().isEmpty);
    await settle(tester);

    expect(find.text('Problem'), findsNothing);
    expect(find.text(HomeScreen.allSentText), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('coming_back_from_the_scanner_loads_the_config_at_most_once_every_5_minutes', (tester) async {
    await startApp(tester, const [_pod, _invoice]);
    final afterStart = client.configCalls;
    expect(afterStart, greaterThanOrEqualTo(1));

    void comeToTheFront() {
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    }

    // The scanner is its own activity: every paper is a resume. Within 5 minutes, no new load.
    for (var i = 0; i < 10; i++) {
      now = now.add(const Duration(seconds: 25));
      comeToTheFront();
      await settle(tester);
    }
    expect(client.configCalls, afterStart);

    now = DateTime(2026, 11, 19, 9, 5); // 5 minutes after the start.
    comeToTheFront();
    await settle(tester, () => client.configCalls == afterStart + 1);
    expect(client.configCalls, afterStart + 1);

    now = now.add(const Duration(minutes: 4, seconds: 59));
    comeToTheFront();
    await settle(tester);
    expect(client.configCalls, afterStart + 1);
    await closeApp(tester);
  });

  testWidgets('the_status_never_says_all_sent_while_a_refused_scan_is_on_the_phone', (tester) async {
    await startApp(tester, const [_pod, _general]);
    client.outcomes
      ..add(const UploadRejected('The document is too large to send.'))
      ..add(const UploadRejected('The document is too large to send.'));
    scanner.script
      ..add(await document(tester, 'big-1'))
      ..add(await document(tester, 'big-2'));

    await tester.tap(find.text('Proof of delivery'));
    await settle(tester, () => queued().length == 2 && queued().every((item) => item.state == QueuedDocument.failed));
    await settle(tester);

    expect(find.text(HomeScreen.allSentText), findsNothing);
    expect(find.text('2 scans not sent — see below'), findsOneWidget);

    // One removed: one left, still not "all sent".
    await reveal(tester, find.text('Remove').first);
    await tester.tap(find.text('Remove').first);
    await tester.pumpAndSettle();
    await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Remove')));
    await settle(tester, () => queued().length == 1);
    await settle(tester);
    await tester.scrollUntilVisible(
      find.text('1 scan not sent — see below'),
      -80,
      scrollable: find.byType(Scrollable).last,
    );
    expect(find.text('1 scan not sent — see below'), findsOneWidget);
    expect(find.text(HomeScreen.allSentText), findsNothing);
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

    await reveal(tester, find.text(ReviewScreen.nextLabel));
    await tester.tap(find.text(ReviewScreen.nextLabel));
    await settle(tester, () => client.uploads.isNotEmpty && scanner.calls == 2);
    await settle(tester);
    expect(tester.takeException(), isNull);
    await closeApp(tester);
  });
}
