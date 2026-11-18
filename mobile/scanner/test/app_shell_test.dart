import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/app.dart';
import 'package:scanner/device/device_session.dart';
import 'package:scanner/device/device_store.dart';
import 'package:scanner/device/setup_code.dart';
import 'package:scanner/scanner/document_scanner.dart';
import 'package:scanner/screens/setup_screen.dart';
import 'package:scanner/screens/staff_picker_screen.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

class _NoKeyStore implements DeviceStore {
  @override
  Future<DeviceCredentials?> read() async => null;

  @override
  Future<void> write(DeviceCredentials credentials) async {}

  @override
  Future<void> deleteKey() async {}

  @override
  Future<void> setDeviceName(String name) async {}
}

class _NoEnroller implements Enroller {
  @override
  Future<EnrolOutcome> enrol(SetupCode code) async => const EnrolRefused(EnrolMessages.invalidCode);
}

class _NoScanner implements DocumentScanner {
  @override
  Future<ScannedDocument?> scan() async => null;
}

void main() {
  setUp(() => SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty());

  testWidgets('a_phone_without_a_key_shows_the_setup_button', (tester) async {
    final settings = AppSettings(SharedPreferencesAsync());
    final session = DeviceSession(
      store: _NoKeyStore(),
      enroller: _NoEnroller(),
      settings: settings,
      queueDirectory: () async => Directory.systemTemp,
      clientFor: (_) => throw StateError('no key'),
    );

    await tester.pumpWidget(ScannerApp(
      services: AppServices(session: session, scanner: _NoScanner(), settings: settings),
    ));
    await tester.pumpAndSettle();

    expect(find.text('Scan setup code'), findsOneWidget);
    expect(session.phase, SessionPhase.needsSetup);
  });

  testWidgets('the_setup_screen_shows_why_the_phone_must_be_set_up_again', (tester) async {
    final session = DeviceSession(
      store: _NoKeyStore(),
      enroller: _NoEnroller(),
      settings: AppSettings(SharedPreferencesAsync()),
      queueDirectory: () async => Directory.systemTemp,
      clientFor: (_) => throw StateError('no key'),
    );

    await tester.pumpWidget(MaterialApp(
      home: SetupScreen(session: session, message: DeviceSession.removedMessage),
    ));

    expect(find.text('This phone was removed. Ask an admin for a new setup code.'), findsOneWidget);
  });

  testWidgets('the_staff_picker_returns_the_tapped_name', (tester) async {
    StaffMember? chosen;
    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => TextButton(
          onPressed: () async => chosen = await Navigator.of(context).push<StaffMember>(
            MaterialPageRoute(
              builder: (_) => const StaffPickerScreen(
                staff: [StaffMember(id: 's-1', name: 'Anna'), StaffMember(id: 's-2', name: 'Thabo')],
              ),
            ),
          ),
          child: const Text('open'),
        ),
      ),
    ));

    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(find.text('Who is scanning?'), findsOneWidget);
    await tester.tap(find.text('Thabo'));
    await tester.pumpAndSettle();

    expect(chosen?.id, 's-2');
  });
}
