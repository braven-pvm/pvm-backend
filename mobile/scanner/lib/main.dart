import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/documents_api.dart';
import 'app.dart';
import 'auth/auth_service.dart';
import 'config.dart';
import 'scanner/mlkit_document_scanner.dart';
import 'settings/app_settings.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final config = AppConfig.fromEnvironment();
  final auth = MsalAuthService(config);
  final services = AppServices(
    config: config,
    auth: auth,
    scanner: MlKitDocumentScanner(),
    settings: AppSettings(SharedPreferencesAsync()),
    api: config.missing.isEmpty ? DocumentsApi(baseUri: config.apiBaseUri, tokens: auth) : null,
  );

  runApp(ScannerApp(services: services));
}
