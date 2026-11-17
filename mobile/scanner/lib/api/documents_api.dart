import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

/// Gives the bearer token for the API. Returns null when the user must sign in again.
abstract interface class AccessTokenSource {
  Future<String?> accessToken();
}

/// One document type the backend accepts (`GET /api/documents/types`).
class DocumentType {
  const DocumentType({required this.code, required this.displayName});

  final String code;
  final String displayName;
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

/// No valid token, even after a second silent attempt: the user must sign in again.
final class UploadSignInRequired extends UploadOutcome {
  const UploadSignInRequired();
}

/// 403: the signed-in user may not upload documents.
final class UploadAccessDenied extends UploadOutcome {
  const UploadAccessDenied();
}

/// Sends one PDF to the backend.
abstract interface class DocumentUploader {
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
  });
}

/// Client for the document intake endpoints of the PVM API.
class DocumentsApi implements DocumentUploader {
  DocumentsApi({
    required this.baseUri,
    required this.tokens,
    http.Client? client,
    this.timeout = const Duration(minutes: 2),
  }) : _client = client ?? http.Client();

  final Uri baseUri;
  final AccessTokenSource tokens;
  final http.Client _client;
  final Duration timeout;

  Uri _endpoint(String path) {
    final basePath = baseUri.path.endsWith('/') ? baseUri.path : '${baseUri.path}/';
    return baseUri.replace(path: '$basePath$path');
  }

  /// The document types the app may offer. Throws [DocumentsApiException] on any failure.
  Future<List<DocumentType>> documentTypes() async {
    final token = await tokens.accessToken();
    if (token == null) {
      throw const DocumentsApiException('Sign in again to load the document types.');
    }

    final http.Response response;
    try {
      response = await _client
          .get(_endpoint('api/documents/types'), headers: {'Authorization': 'Bearer $token'})
          .timeout(timeout);
    } on Exception catch (error) {
      throw DocumentsApiException('The document types could not be loaded: ${_describe(error)}');
    }

    if (response.statusCode != 200) {
      throw DocumentsApiException('The document types could not be loaded (HTTP ${response.statusCode}).');
    }

    final list = jsonDecode(response.body) as List<dynamic>;
    return [
      for (final entry in list.cast<Map<String, dynamic>>())
        DocumentType(code: entry['code'] as String, displayName: entry['displayName'] as String),
    ];
  }

  @override
  Future<UploadOutcome> upload({
    required File pdf,
    required String clientDocumentId,
    required String documentType,
    required int pageCount,
  }) async {
    for (var attempt = 1; attempt <= 2; attempt++) {
      final String? token;
      try {
        token = await tokens.accessToken();
      } on Exception catch (error) {
        return UploadRetryLater('The sign-in could not be renewed: ${_describe(error)}');
      }

      if (token == null) {
        return const UploadSignInRequired();
      }

      final outcome = await _send(token, pdf, clientDocumentId, documentType, pageCount);
      if (outcome != null) {
        return outcome;
      }
      // 401: get a token again, silently, once. A second 401 means the user must sign in.
    }

    return const UploadSignInRequired();
  }

  /// Returns null for a 401.
  Future<UploadOutcome?> _send(
    String token,
    File pdf,
    String clientDocumentId,
    String documentType,
    int pageCount,
  ) async {
    final request = http.MultipartRequest('POST', _endpoint('api/documents'))
      ..headers['Authorization'] = 'Bearer $token'
      ..fields['clientDocumentId'] = clientDocumentId
      ..fields['documentType'] = documentType
      ..fields['pageCount'] = '$pageCount';

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
        return null;
      case 403:
        return const UploadAccessDenied();
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

  static String _detail(http.Response response) {
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

  static String _describe(Exception error) => switch (error) {
        SocketException(:final message) => message,
        TimeoutException() => 'the server did not answer in time',
        http.ClientException(:final message) => message,
        _ => error.toString(),
      };
}

class DocumentsApiException implements Exception {
  const DocumentsApiException(this.message);

  final String message;

  @override
  String toString() => message;
}
