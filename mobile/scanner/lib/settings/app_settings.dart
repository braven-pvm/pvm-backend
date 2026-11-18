import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../api/device_api.dart';

/// Small values the app remembers between runs. Nothing secret: the device key is in
/// encrypted storage (`DeviceStore`).
class AppSettings {
  AppSettings(this._preferences);

  static const defaultDocumentType = 'POD';
  static const _documentTypeKey = 'documentType';
  static const _staffIdKey = 'staffId';
  static const _configKey = 'deviceConfig';

  final SharedPreferencesAsync _preferences;

  Future<String> documentType() async =>
      await _preferences.getString(_documentTypeKey) ?? defaultDocumentType;

  Future<void> setDocumentType(String code) => _preferences.setString(_documentTypeKey, code);

  /// The staff name chosen last ("Who is scanning?"), or null.
  Future<String?> staffId() => _preferences.getString(_staffIdKey);

  Future<void> setStaffId(String? id) =>
      id == null ? _preferences.remove(_staffIdKey) : _preferences.setString(_staffIdKey, id);

  /// The last device config the server sent, for a start without a connection.
  Future<DeviceConfig?> cachedConfig() async {
    final json = await _preferences.getString(_configKey);
    if (json == null) {
      return null;
    }
    try {
      return DeviceConfig.fromJson(jsonDecode(json) as Map<String, dynamic>);
    } on Object {
      return null;
    }
  }

  Future<void> setCachedConfig(DeviceConfig? config) => config == null
      ? _preferences.remove(_configKey)
      : _preferences.setString(_configKey, jsonEncode(config.toJson()));
}
