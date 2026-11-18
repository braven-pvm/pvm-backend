import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import '../device/setup_code.dart';

/// One document type the backend accepts.
class DocumentType {
  const DocumentType({required this.code, required this.displayName});

  final String code;
  final String displayName;

  Map<String, Object?> toJson() => {'code': code, 'displayName': displayName};

  factory DocumentType.fromJson(Map<String, dynamic> json) =>
      DocumentType(code: json['code'] as String, displayName: json['displayName'] as String);
}

/// One active staff name (no password, no email).
class StaffMember {
  const StaffMember({required this.id, required this.name});

  final String id;
  final String name;

  Map<String, Object?> toJson() => {'id': id, 'name': name};

  factory StaffMember.fromJson(Map<String, dynamic> json) =>
      StaffMember(id: json['id'] as String, name: json['name'] as String);
}

/// `GET /api/device/config`.
class DeviceConfig {
  const DeviceConfig({required this.deviceName, required this.documentTypes, required this.staff});

  final String deviceName;
  final List<DocumentType> documentTypes;

  /// Active names, ordered by name. Empty: the app does not ask who is scanning.
  final List<StaffMember> staff;

  Map<String, Object?> toJson() => {
        'deviceName': deviceName,
        'documentTypes': [for (final type in documentTypes) type.toJson()],
        'staff': [for (final member in staff) member.toJson()],
      };

  factory DeviceConfig.fromJson(Map<String, dynamic> json) => DeviceConfig(
        deviceName: json['deviceName'] as String? ?? '',
        documentTypes: [
          for (final entry in (json['documentTypes'] as List<dynamic>? ?? const []).cast<Map<String, dynamic>>())
            DocumentType.fromJson(entry),
        ],
        staff: [
          for (final entry in (json['staff'] as List<dynamic>? ?? const []).cast<Map<String, dynamic>>())
            StaffMember.fromJson(entry),
        ],
      );
}

/// The result of one upload attempt.
sealed class UploadOutcome {
  const UploadOutcome();
}

/// 201 for a new document, 200 for a client document ID the backend already has. The status
/// alone counts; [documentId] and [state] are null when the body cannot be read.
final class UploadAccepted extends UploadOutcome {
  const UploadAccepted({this.documentId, this.state});

  final String? documentId;
  final String? state;
}

/// 400 or 413: the backend will never accept this file. Retrying does not help.
final class UploadRejected extends UploadOutcome {
  const UploadRejected(this.reason);

  final String reason;
}

/// A network error or a server error: try again later.
final class UploadRetryLater extends UploadOutcome {
  const UploadRetryLater(this.reason);

  final String reason;
}

/// 401: the device was removed or set up again elsewhere. The phone needs a new setup code.
final class UploadDeviceRevoked extends UploadOutcome {
  const UploadDeviceRevoked();
}

/// Sends one PDF to the backend.
abstract interface class DocumentUploader {
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  });
}

/// The calls an enrolled phone makes with its device key.
abstract interface class DeviceClient implements DocumentUploader {
  /// Throws [DeviceRevokedException] on 401 and [DeviceApiException] on any other failure.
  Future<DeviceConfig> config();
}

/// The result of `POST /api/device/enrol`.
sealed class EnrolOutcome {
  const EnrolOutcome();
}

final class EnrolSucceeded extends EnrolOutcome {
  const EnrolSucceeded({required this.deviceKey, required this.deviceName});

  final String deviceKey;
  final String deviceName;
}

/// The enrolment did not work; [message] is for the person holding the phone.
final class EnrolRefused extends EnrolOutcome {
  const EnrolRefused(this.message);

  final String message;
}

abstract final class EnrolMessages {
  static const invalidCode = 'This setup code is not valid. Ask an admin for a new one.';
  static const tooManyTries = 'Too many tries. Wait one minute.';
  static const noConnection = 'No connection. Check the Wi-Fi and try again.';
}

/// Exchanges a setup code for a device key.
abstract interface class Enroller {
  Future<EnrolOutcome> enrol(SetupCode code);
}

// Every device request sets followRedirects = false: a redirect would carry the device key (or
// the setup code) to another address. A 3xx is then an ordinary failed status.
Uri _endpoint(Uri baseUri, String path) {
  final basePath = baseUri.path.endsWith('/') ? baseUri.path : '${baseUri.path}/';
  return baseUri.replace(path: '$basePath$path');
}

String _describe(Exception error) => switch (error) {
      SocketException(:final message) => message,
      TimeoutException() => 'the server did not answer in time',
      http.ClientException(:final message) => message,
      _ => error.toString(),
    };

String _detail(http.Response response) {
  try {
    final body = jsonDecode(response.body);
    if (body is Map<String, dynamic> && body['detail'] is String) {
      return body['detail'] as String;
    }
  } on FormatException {
    // Not JSON: no detail to show.
  }
  return '';
}

/// `POST {api}/api/device/enrol` (no key; rate-limited by the backend).
class DeviceEnrolClient implements Enroller {
  DeviceEnrolClient({http.Client? client, this.timeout = const Duration(seconds: 30)})
      : _client = client ?? http.Client();

  final http.Client _client;
  final Duration timeout;

  @override
  Future<EnrolOutcome> enrol(SetupCode code) async {
    final http.Response response;
    try {
      final request = http.Request('POST', _endpoint(code.api, 'api/device/enrol'))
        ..followRedirects = false
        ..headers['Content-Type'] = 'application/json'
        ..body = jsonEncode({'code': code.code});
      response = await http.Response.fromStream(await _client.send(request).timeout(timeout)).timeout(timeout);
    } on Exception {
      return const EnrolRefused(EnrolMessages.noConnection);
    }

    switch (response.statusCode) {
      case 200:
        try {
          final body = jsonDecode(response.body);
          if (body is Map<String, dynamic> && body['deviceKey'] is String && body['deviceKey'] != '') {
            return EnrolSucceeded(
              deviceKey: body['deviceKey'] as String,
              deviceName: body['deviceName'] is String ? body['deviceName'] as String : '',
            );
          }
        } on FormatException {
          // Falls through: not the answer of a PVM server.
        }
        return const EnrolRefused('The server gave an answer the app does not understand. Ask IT.');
      case 400:
        return const EnrolRefused(EnrolMessages.invalidCode);
      case 429:
        return const EnrolRefused(EnrolMessages.tooManyTries);
      default:
        return EnrolRefused('The server had a problem (HTTP ${response.statusCode}). Try again.');
    }
  }
}

/// Client for the device endpoints, with `Authorization: Device <key>`.
class DeviceApi implements DeviceClient {
  DeviceApi({
    required this.baseUri,
    required String deviceKey,
    http.Client? client,
    this.timeout = const Duration(minutes: 2),
  })  : _authorization = 'Device $deviceKey',
        _client = client ?? http.Client();

  static const _staffFieldMarker = "'staffId'";

  final Uri baseUri;
  final String _authorization;
  final http.Client _client;
  final Duration timeout;

  @override
  Future<DeviceConfig> config() async {
    final http.Response response;
    try {
      final request = http.Request('GET', _endpoint(baseUri, 'api/device/config'))
        ..followRedirects = false
        ..headers['Authorization'] = _authorization;
      response = await http.Response.fromStream(await _client.send(request).timeout(timeout)).timeout(timeout);
    } on Exception catch (error) {
      throw DeviceApiException('The settings could not be loaded: ${_describe(error)}');
    }

    if (response.statusCode == 401) {
      throw const DeviceRevokedException();
    }
    if (response.statusCode != 200) {
      throw DeviceApiException('The settings could not be loaded (HTTP ${response.statusCode}).');
    }
    try {
      return DeviceConfig.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    } on Object {
      throw const DeviceApiException('The settings from the server could not be read.');
    }
  }

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
    String? staffId,
  }) async {
    final outcome = await _send(pdf, clientDocumentId, documentType, pageCount, staffId);
    // A staff name turned off while the scan waited: the document matters more than the name.
    if (staffId != null && outcome is UploadRejected && outcome.reason.contains(_staffFieldMarker)) {
      return _send(pdf, clientDocumentId, documentType, pageCount, null);
    }
    return outcome;
  }

  Future<UploadOutcome> _send(
    File pdf,
    String clientDocumentId,
    String documentType,
    int pageCount,
    String? staffId,
  ) async {
    final request = http.MultipartRequest('POST', _endpoint(baseUri, 'api/device/documents'))
      ..followRedirects = false
      ..headers['Authorization'] = _authorization
      ..fields['clientDocumentId'] = clientDocumentId
      ..fields['documentType'] = documentType
      ..fields['pageCount'] = '$pageCount';
    if (staffId != null) {
      request.fields['staffId'] = staffId;
    }

    final http.Response response;
    try {
      request.files.add(
        http.MultipartFile.fromBytes(
          'file',
          await pdf.readAsBytes(),
          filename: '$clientDocumentId.pdf',
          contentType: http.MediaType('application', 'pdf'),
        ),
      );
      response = await http.Response.fromStream(await _client.send(request).timeout(timeout));
    } on FileSystemException catch (error) {
      return UploadRejected('The scanned file could not be read on this phone: ${error.message}');
    } on Exception catch (error) {
      return UploadRetryLater('No connection to the server: ${_describe(error)}');
    }

    switch (response.statusCode) {
      case 200:
      case 201:
        return _accepted(response);
      case 400:
        return UploadRejected('The server did not accept this document. ${_detail(response)}'.trim());
      case 413:
        return const UploadRejected('The document is too large to send. Scan it again with fewer pages.');
      case 401:
        return const UploadDeviceRevoked();
      default:
        return UploadRetryLater('The server had a problem (HTTP ${response.statusCode}). ${_detail(response)}'.trim());
    }
  }

  /// The receipt; the body is read best-effort, because the status already says the backend has it.
  static UploadAccepted _accepted(http.Response response) {
    try {
      final body = jsonDecode(response.body);
      if (body is Map<String, dynamic>) {
        return UploadAccepted(
          documentId: body['documentId'] is String ? body['documentId'] as String : null,
          state: body['state'] is String ? body['state'] as String : null,
        );
      }
    } on FormatException {
      // Not JSON.
    }
    return const UploadAccepted();
  }
}

/// 401 from a device endpoint: the device was removed or its key replaced.
class DeviceRevokedException implements Exception {
  const DeviceRevokedException();

  @override
  String toString() => 'This phone was removed.';
}

class DeviceApiException implements Exception {
  const DeviceApiException(this.message);

  final String message;

  @override
  String toString() => message;
}
