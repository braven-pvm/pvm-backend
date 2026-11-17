import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:scanner/api/documents_api.dart';

class FakeTokens implements AccessTokenSource {
  FakeTokens(this.tokens);

  final List<String?> tokens;
  int calls = 0;

  @override
  Future<String?> accessToken() async {
    final token = calls < tokens.length ? tokens[calls] : tokens.last;
    calls++;
    return token;
  }
}

class FailingTokens implements AccessTokenSource {
  @override
  Future<String?> accessToken() async => throw Exception('no network for the token endpoint');
}

class CapturedUpload {
  CapturedUpload(this.request, this.body);

  final http.BaseRequest request;
  final String body;
}

void main() {
  late Directory root;
  late File pdf;
  final baseUri = Uri.parse('https://api.example.test/');

  setUp(() {
    root = Directory.systemTemp.createTempSync('documents_api_test');
    pdf = File('${root.path}/doc.pdf')..writeAsBytesSync(utf8.encode('%PDF-1.7 body'));
  });

  tearDown(() => root.deleteSync(recursive: true));

  DocumentsApi api(MockClient client, FakeTokens tokens) =>
      DocumentsApi(baseUri: baseUri, tokens: tokens, client: client);

  Future<UploadOutcome> upload(DocumentsApi documentsApi) => documentsApi.upload(
        pdf: pdf,
        clientDocumentId: '3f2b1c4d-0000-4000-8000-000000000001',
        documentType: 'POD',
        pageCount: 2,
      );

  http.Response problem(int status, String detail) => http.Response(
        jsonEncode({'title': 'Error', 'status': status, 'detail': detail}),
        status,
        headers: {'content-type': 'application/problem+json'},
      );

  test('upload_posts_multipart_with_bearer_token_and_fields', () async {
    final captured = <CapturedUpload>[];
    final client = MockClient((request) async {
      captured.add(CapturedUpload(request, request.body));
      return http.Response(jsonEncode({'documentId': 'd-1', 'state': 'Received'}), 201);
    });

    final outcome = await upload(api(client, FakeTokens(['token-1'])));

    expect(outcome, isA<UploadAccepted>());
    expect((outcome as UploadAccepted).documentId, 'd-1');
    expect(outcome.state, 'Received');
    final request = captured.single.request;
    expect(request.method, 'POST');
    expect(request.url.toString(), 'https://api.example.test/api/documents');
    expect(request.headers['Authorization'], 'Bearer token-1');
    expect(request.headers['content-type'], startsWith('multipart/form-data'));
    final body = captured.single.body;
    expect(body, contains('name="clientDocumentId"\r\n\r\n3f2b1c4d-0000-4000-8000-000000000001'));
    expect(body, contains('name="documentType"\r\n\r\nPOD'));
    expect(body, contains('name="pageCount"\r\n\r\n2'));
    expect(body, contains('name="file"; filename="3f2b1c4d-0000-4000-8000-000000000001.pdf"'));
    expect(body, contains('content-type: application/pdf'));
    expect(body, contains('%PDF-1.7 body'));
  });

  test('upload_of_a_known_document_200_is_accepted', () async {
    final client = MockClient((_) async => http.Response(jsonEncode({'documentId': 'd-9', 'state': 'Filed'}), 200));

    final outcome = await upload(api(client, FakeTokens(['t'])));

    expect(outcome, isA<UploadAccepted>());
    expect((outcome as UploadAccepted).state, 'Filed');
  });

  test('upload_201_without_a_readable_body_is_still_accepted', () async {
    final client = MockClient((_) async => http.Response('<html>proxy page</html>', 201));

    final outcome = await upload(api(client, FakeTokens(['t'])));

    expect(outcome, isA<UploadAccepted>());
    expect((outcome as UploadAccepted).documentId, isNull);
  });

  test('upload_400_is_rejected_with_the_backend_reason', () async {
    final client = MockClient((_) async => problem(400, 'The file is not a PDF.'));

    final outcome = await upload(api(client, FakeTokens(['t'])));

    expect(outcome, isA<UploadRejected>());
    expect((outcome as UploadRejected).reason, contains('The file is not a PDF.'));
  });

  test('upload_413_is_rejected_as_too_large', () async {
    final client = MockClient((_) async => problem(413, 'The file is larger than the limit of 15728640 bytes.'));

    final outcome = await upload(api(client, FakeTokens(['t'])));

    expect(outcome, isA<UploadRejected>());
    expect((outcome as UploadRejected).reason, contains('too large'));
  });

  test('upload_401_gets_a_new_token_once_and_retries', () async {
    final authHeaders = <String?>[];
    final client = MockClient((request) async {
      authHeaders.add(request.headers['Authorization']);
      return authHeaders.length == 1
          ? http.Response('', 401)
          : http.Response(jsonEncode({'documentId': 'd-2', 'state': 'Received'}), 201);
    });
    final tokens = FakeTokens(['old', 'new']);

    final outcome = await upload(api(client, tokens));

    expect(outcome, isA<UploadAccepted>());
    expect(authHeaders, ['Bearer old', 'Bearer new']);
    expect(tokens.calls, 2);
  });

  test('upload_401_twice_asks_for_sign_in', () async {
    var requests = 0;
    final client = MockClient((_) async {
      requests++;
      return http.Response('', 401);
    });

    final outcome = await upload(api(client, FakeTokens(['a', 'b'])));

    expect(outcome, isA<UploadSignInRequired>());
    expect(requests, 2);
  });

  test('upload_without_a_token_asks_for_sign_in_and_sends_nothing', () async {
    var requests = 0;
    final client = MockClient((_) async {
      requests++;
      return http.Response('', 201);
    });

    final outcome = await upload(api(client, FakeTokens([null])));

    expect(outcome, isA<UploadSignInRequired>());
    expect(requests, 0);
  });

  test('a_token_renewal_error_retries_later', () async {
    final client = MockClient((_) async => http.Response('', 201));

    final outcome = await DocumentsApi(baseUri: baseUri, tokens: FailingTokens(), client: client).upload(
      pdf: pdf,
      clientDocumentId: '3f2b1c4d-0000-4000-8000-000000000001',
      documentType: 'POD',
      pageCount: 1,
    );

    expect(outcome, isA<UploadRetryLater>());
  });

  test('upload_403_is_access_denied', () async {
    final client = MockClient((_) async => http.Response('', 403));

    final outcome = await upload(api(client, FakeTokens(['t'])));

    expect(outcome, isA<UploadAccessDenied>());
  });

  test('upload_500_and_network_errors_retry_later', () async {
    final serverError = MockClient((_) async => problem(500, 'The user could not be resolved.'));
    final offline = MockClient((_) async => throw const SocketException('Failed host lookup'));

    expect(await upload(api(serverError, FakeTokens(['t']))), isA<UploadRetryLater>());
    expect(await upload(api(offline, FakeTokens(['t']))), isA<UploadRetryLater>());
  });

  test('document_types_are_read_with_bearer_token', () async {
    http.Request? seen;
    final client = MockClient((request) async {
      seen = request;
      return http.Response(
        jsonEncode([
          {'code': 'POD', 'displayName': 'Proof of delivery'},
        ]),
        200,
      );
    });

    final types = await api(client, FakeTokens(['t'])).documentTypes();

    expect(seen!.method, 'GET');
    expect(seen!.url.toString(), 'https://api.example.test/api/documents/types');
    expect(seen!.headers['Authorization'], 'Bearer t');
    expect(types.single.code, 'POD');
    expect(types.single.displayName, 'Proof of delivery');
  });

  test('a_base_address_with_a_path_keeps_the_path', () async {
    Uri? seen;
    final client = MockClient((request) async {
      seen = request.url;
      return http.Response('[]', 200);
    });

    await DocumentsApi(
      baseUri: Uri.parse('https://host.example.test/pvm'),
      tokens: FakeTokens(['t']),
      client: client,
    ).documentTypes();

    expect(seen.toString(), 'https://host.example.test/pvm/api/documents/types');
  });
}
