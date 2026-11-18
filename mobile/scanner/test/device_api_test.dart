import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/device/setup_code.dart';

void main() {
  late Directory root;
  late File pdf;
  final baseUri = Uri.parse('https://api.example.test');
  const key = 'pvmd_0123456789abcdefghijklmnopqrstuvwxyzABCDEFG';
  const clientDocumentId = '3f2b1c4d-0000-4000-8000-000000000001';

  setUp(() {
    root = Directory.systemTemp.createTempSync('device_api_test');
    pdf = File('${root.path}/doc.pdf')..writeAsBytesSync(utf8.encode('%PDF-1.7 body'));
  });

  tearDown(() => root.deleteSync(recursive: true));

  DeviceApi api(MockClient client) => DeviceApi(baseUri: baseUri, deviceKey: key, client: client);

  Future<UploadOutcome> upload(DeviceApi deviceApi, {String? staffId}) => deviceApi.upload(
        pdf: pdf,
        clientDocumentId: clientDocumentId,
        documentType: 'POD',
        pageCount: 2,
        staffId: staffId,
      );

  http.Response problem(int status, String detail) => http.Response(
        jsonEncode({'title': 'Error', 'status': status, 'detail': detail}),
        status,
        headers: {'content-type': 'application/problem+json'},
      );

  group('enrol', () {
    final setupCode = SetupCode(api: Uri.parse('https://api.example.test'), code: 'abc123');

    test('200_returns_the_device_key_and_name', () async {
      http.Request? seen;
      final client = MockClient((request) async {
        seen = request;
        return http.Response(jsonEncode({'deviceKey': key, 'deviceName': 'Dispatch phone 1'}), 200);
      });

      final outcome = await DeviceEnrolClient(client: client).enrol(setupCode);

      expect(outcome, isA<EnrolSucceeded>());
      outcome as EnrolSucceeded;
      expect(outcome.deviceKey, key);
      expect(outcome.deviceName, 'Dispatch phone 1');
      expect(seen!.method, 'POST');
      expect(seen!.url.toString(), 'https://api.example.test/api/device/enrol');
      expect(seen!.headers['content-type'], startsWith('application/json'));
      expect(jsonDecode(seen!.body), {'code': 'abc123'});
      expect(seen!.headers.containsKey('Authorization'), isFalse);
    });

    test('400_says_the_code_is_not_valid', () async {
      final client = MockClient((_) async => problem(400, 'The setup code is not valid.'));

      final outcome = await DeviceEnrolClient(client: client).enrol(setupCode);

      expect(outcome, isA<EnrolRefused>());
      expect((outcome as EnrolRefused).message, 'This setup code is not valid. Ask an admin for a new one.');
    });

    test('429_says_wait_one_minute', () async {
      final client = MockClient((_) async => http.Response('', 429));

      final outcome = await DeviceEnrolClient(client: client).enrol(setupCode);

      expect((outcome as EnrolRefused).message, 'Too many tries. Wait one minute.');
    });

    test('a_network_error_or_timeout_says_no_connection', () async {
      final offline = MockClient((_) async => throw const SocketException('Failed host lookup'));
      final slow = MockClient((_) => Completer<http.Response>().future);

      final first = await DeviceEnrolClient(client: offline).enrol(setupCode);
      final second = await DeviceEnrolClient(client: slow, timeout: const Duration(milliseconds: 10)).enrol(setupCode);

      const message = 'No connection. Check the Wi-Fi and try again.';
      expect((first as EnrolRefused).message, message);
      expect((second as EnrolRefused).message, message);
    });

    test('a_server_error_or_an_unreadable_200_is_refused_with_the_status', () async {
      final serverError = MockClient((_) async => http.Response('', 503));
      final badBody = MockClient((_) async => http.Response('<html></html>', 200));

      final first = await DeviceEnrolClient(client: serverError).enrol(setupCode);
      final second = await DeviceEnrolClient(client: badBody).enrol(setupCode);

      expect((first as EnrolRefused).message, contains('503'));
      expect(second, isA<EnrolRefused>());
    });
  });

  group('config', () {
    test('is_read_with_the_device_key', () async {
      http.Request? seen;
      final client = MockClient((request) async {
        seen = request;
        return http.Response(
          jsonEncode({
            'deviceName': 'Dispatch phone 1',
            'documentTypes': [
              {'code': 'POD', 'displayName': 'Proof of delivery'},
            ],
            'staff': [
              {'id': 's-1', 'name': 'Anna'},
              {'id': 's-2', 'name': 'Thabo'},
            ],
          }),
          200,
        );
      });

      final config = await api(client).config();

      expect(seen!.method, 'GET');
      expect(seen!.url.toString(), 'https://api.example.test/api/device/config');
      expect(seen!.headers['Authorization'], 'Device $key');
      expect(config.deviceName, 'Dispatch phone 1');
      expect(config.documentTypes.single.code, 'POD');
      expect(config.documentTypes.single.displayName, 'Proof of delivery');
      expect(config.staff.map((member) => member.name), ['Anna', 'Thabo']);
      expect(config.staff.first.id, 's-1');
    });

    test('survives_a_json_round_trip', () async {
      const config = DeviceConfig(
        deviceName: 'Dispatch phone 1',
        documentTypes: [DocumentType(code: 'POD', displayName: 'Proof of delivery')],
        staff: [StaffMember(id: 's-1', name: 'Anna')],
      );

      final copy = DeviceConfig.fromJson(jsonDecode(jsonEncode(config.toJson())) as Map<String, dynamic>);

      expect(copy.deviceName, 'Dispatch phone 1');
      expect(copy.documentTypes.single.code, 'POD');
      expect(copy.staff.single.id, 's-1');
    });

    test('401_means_the_device_was_revoked', () async {
      final client = MockClient((_) async => http.Response('', 401));

      await expectLater(api(client).config(), throwsA(isA<DeviceRevokedException>()));
    });

    test('other_failures_throw_a_device_api_exception', () async {
      final offline = MockClient((_) async => throw const SocketException('Failed host lookup'));
      final serverError = MockClient((_) async => http.Response('', 500));
      final badBody = MockClient((_) async => http.Response('<html></html>', 200));

      await expectLater(api(offline).config(), throwsA(isA<DeviceApiException>()));
      await expectLater(api(serverError).config(), throwsA(isA<DeviceApiException>()));
      await expectLater(api(badBody).config(), throwsA(isA<DeviceApiException>()));
    });
  });

  group('upload', () {
    test('posts_multipart_to_the_device_endpoint_with_the_device_key_and_fields', () async {
      final captured = <({http.BaseRequest request, String body})>[];
      final client = MockClient((request) async {
        captured.add((request: request, body: request.body));
        return http.Response(jsonEncode({'documentId': 'd-1', 'state': 'Received'}), 201);
      });

      final outcome = await upload(api(client), staffId: 'staff-1');

      expect(outcome, isA<UploadAccepted>());
      expect((outcome as UploadAccepted).documentId, 'd-1');
      expect(outcome.state, 'Received');
      final request = captured.single.request;
      expect(request.method, 'POST');
      expect(request.url.toString(), 'https://api.example.test/api/device/documents');
      expect(request.headers['Authorization'], 'Device $key');
      expect(request.headers['content-type'], startsWith('multipart/form-data'));
      final body = captured.single.body;
      expect(body, contains('name="clientDocumentId"\r\n\r\n$clientDocumentId'));
      expect(body, contains('name="documentType"\r\n\r\nPOD'));
      expect(body, contains('name="pageCount"\r\n\r\n2'));
      expect(body, contains('name="staffId"\r\n\r\nstaff-1'));
      expect(body, contains('name="file"; filename="$clientDocumentId.pdf"'));
      expect(body, contains('content-type: application/pdf'));
      expect(body, contains('%PDF-1.7 body'));
    });

    test('without_a_staff_id_sends_no_staff_field', () async {
      String? body;
      final client = MockClient((request) async {
        body = request.body;
        return http.Response('', 201);
      });

      await upload(api(client));

      expect(body, isNot(contains('name="staffId"')));
    });

    test('200_for_a_known_document_is_accepted', () async {
      final client = MockClient((_) async => http.Response(jsonEncode({'documentId': 'd-9', 'state': 'Filed'}), 200));

      final outcome = await upload(api(client));

      expect((outcome as UploadAccepted).state, 'Filed');
    });

    test('201_without_a_readable_body_is_still_accepted', () async {
      final client = MockClient((_) async => http.Response('<html>proxy page</html>', 201));

      final outcome = await upload(api(client));

      expect((outcome as UploadAccepted).documentId, isNull);
    });

    test('400_is_rejected_with_the_backend_reason', () async {
      final client = MockClient((_) async => problem(400, 'The file is not a PDF.'));

      final outcome = await upload(api(client));

      expect((outcome as UploadRejected).reason, contains('The file is not a PDF.'));
    });

    test('400_for_a_staff_name_that_was_turned_off_sends_again_without_the_name', () async {
      final bodies = <String>[];
      final client = MockClient((request) async {
        bodies.add(request.body);
        return bodies.length == 1
            ? problem(400, "The form field 'staffId' must be the ID of an active staff name.")
            : http.Response(jsonEncode({'documentId': 'd-3', 'state': 'Received'}), 201);
      });

      final outcome = await upload(api(client), staffId: 'gone');

      expect(outcome, isA<UploadAccepted>());
      expect(bodies, hasLength(2));
      expect(bodies[0], contains('name="staffId"'));
      expect(bodies[1], isNot(contains('name="staffId"')));
      expect(bodies[1], contains('name="clientDocumentId"\r\n\r\n$clientDocumentId'));
    });

    test('413_is_rejected_as_too_large', () async {
      final client = MockClient((_) async => problem(413, 'Too large.'));

      final outcome = await upload(api(client));

      expect((outcome as UploadRejected).reason, contains('too large'));
    });

    test('401_means_the_device_was_revoked_and_is_not_retried', () async {
      var requests = 0;
      final client = MockClient((_) async {
        requests++;
        return http.Response('', 401);
      });

      final outcome = await upload(api(client));

      expect(outcome, isA<UploadDeviceRevoked>());
      expect(requests, 1);
    });

    test('500_403_and_network_errors_retry_later', () async {
      final serverError = MockClient((_) async => problem(500, 'Boom.'));
      final forbidden = MockClient((_) async => http.Response('', 403));
      final offline = MockClient((_) async => throw const SocketException('Failed host lookup'));

      expect(await upload(api(serverError)), isA<UploadRetryLater>());
      expect(await upload(api(forbidden)), isA<UploadRetryLater>());
      expect(await upload(api(offline)), isA<UploadRetryLater>());
    });

    test('a_missing_pdf_is_rejected', () async {
      final client = MockClient((_) async => http.Response('', 201));
      pdf.deleteSync();

      expect(await upload(api(client)), isA<UploadRejected>());
    });
  });

  test('a_base_address_with_a_path_keeps_the_path', () async {
    Uri? seen;
    final client = MockClient((request) async {
      seen = request.url;
      return http.Response(jsonEncode({'deviceName': 'x', 'documentTypes': [], 'staff': []}), 200);
    });

    await DeviceApi(baseUri: Uri.parse('https://host.example.test/pvm/'), deviceKey: key, client: client).config();

    expect(seen.toString(), 'https://host.example.test/pvm/api/device/config');
  });
}
