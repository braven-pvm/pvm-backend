import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/device_api.dart';
import 'app.dart';
import 'device/device_session.dart';
import 'device/device_store.dart';
import 'scanner/mlkit_document_scanner.dart';
import 'settings/app_settings.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final settings = AppSettings(SharedPreferencesAsync());
  final session = DeviceSession(
    store: SecureDeviceStore(),
    enroller: DeviceEnrolClient(),
    settings: settings,
    queueDirectory: () async => Directory('${(await getApplicationDocumentsDirectory()).path}/upload_queue'),
    clientFor: (credentials) => DeviceApi(baseUri: credentials.apiBaseUri, deviceKey: credentials.deviceKey),
  );

  runApp(ScannerApp(
    services: AppServices(session: session, scanner: MlKitDocumentScanner(), settings: settings),
  ));
}
