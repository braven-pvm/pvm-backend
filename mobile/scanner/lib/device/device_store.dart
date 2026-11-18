import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// What the phone keeps after enrolment.
class DeviceCredentials {
  const DeviceCredentials({required this.apiBaseUri, required this.deviceKey, required this.deviceName});

  /// The PVM API address from the setup QR code.
  final Uri apiBaseUri;

  /// The device key, `pvmd_...`; sent as `Authorization: Device <key>`.
  final String deviceKey;

  final String deviceName;
}

/// Keeps the device key, the API address and the device name.
abstract interface class DeviceStore {
  /// The stored credentials, or null when the phone has no key (never set up, removed or reset).
  Future<DeviceCredentials?> read();

  Future<void> write(DeviceCredentials credentials);

  /// Deletes the key only. The address and the name stay; the next enrolment replaces them.
  Future<void> deleteKey();

  Future<void> setDeviceName(String name);
}

/// [DeviceStore] in Android encrypted storage (`flutter_secure_storage`: AES-GCM data, key
/// wrapped by the Android Keystore).
class SecureDeviceStore implements DeviceStore {
  SecureDeviceStore([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();

  static const _keyKey = 'deviceKey';
  static const _apiKey = 'apiBaseUrl';
  static const _nameKey = 'deviceName';

  final FlutterSecureStorage _storage;

  @override
  Future<DeviceCredentials?> read() async {
    final key = await _storage.read(key: _keyKey);
    final api = await _storage.read(key: _apiKey);
    if (key == null || key.isEmpty || api == null) {
      return null;
    }
    final uri = Uri.tryParse(api);
    if (uri == null || uri.scheme != 'https') {
      return null;
    }
    return DeviceCredentials(
      apiBaseUri: uri,
      deviceKey: key,
      deviceName: await _storage.read(key: _nameKey) ?? '',
    );
  }

  @override
  Future<void> write(DeviceCredentials credentials) async {
    // The key last: a phone that stops half way has no key and shows the setup screen.
    await _storage.write(key: _apiKey, value: credentials.apiBaseUri.toString());
    await _storage.write(key: _nameKey, value: credentials.deviceName);
    await _storage.write(key: _keyKey, value: credentials.deviceKey);
  }

  @override
  Future<void> deleteKey() => _storage.delete(key: _keyKey);

  @override
  Future<void> setDeviceName(String name) => _storage.write(key: _nameKey, value: name);
}
