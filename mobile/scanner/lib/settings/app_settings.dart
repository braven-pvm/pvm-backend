import 'package:shared_preferences/shared_preferences.dart';

/// Small values the app remembers between runs.
class AppSettings {
  AppSettings(this._preferences);

  static const defaultDocumentType = 'POD';
  static const _documentTypeKey = 'documentType';

  final SharedPreferencesAsync _preferences;

  Future<String> documentType() async =>
      await _preferences.getString(_documentTypeKey) ?? defaultDocumentType;

  Future<void> setDocumentType(String code) => _preferences.setString(_documentTypeKey, code);
}
