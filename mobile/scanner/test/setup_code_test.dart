import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/device/setup_code.dart';

void main() {
  String payload(Object? v, Object? api, Object? code) => jsonEncode({'v': v, 'api': api, 'code': code});

  test('a_valid_payload_gives_the_api_address_and_code', () {
    final code = SetupCode.tryParse(payload(1, 'https://ca-pvm-api-qa.example.io', 'abcDEF_123-x'));

    expect(code, isNotNull);
    expect(code!.api.toString(), 'https://ca-pvm-api-qa.example.io');
    expect(code.code, 'abcDEF_123-x');
  });

  test('the_qr_string_as_the_backend_makes_it_is_accepted', () {
    const raw = '{"v":1,"api":"https://host.example.test/pvm","code":"Zx9-abc"}';

    final code = SetupCode.tryParse(raw);

    expect(code!.api.toString(), 'https://host.example.test/pvm');
    expect(code.code, 'Zx9-abc');
  });

  test('another_version_is_refused', () {
    expect(SetupCode.tryParse(payload(2, 'https://a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload('1', 'https://a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload(null, 'https://a.example.test', 'c')), isNull);
  });

  test('an_http_or_odd_api_address_is_refused', () {
    expect(SetupCode.tryParse(payload(1, 'http://a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 'ftp://a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 'https://', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 'a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 'https://user:pw@a.example.test', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 'https://a.example.test?x=1', 'c')), isNull);
    expect(SetupCode.tryParse(payload(1, 42, 'c')), isNull);
  });

  test('a_missing_or_empty_code_is_refused', () {
    expect(SetupCode.tryParse(payload(1, 'https://a.example.test', '')), isNull);
    expect(SetupCode.tryParse(payload(1, 'https://a.example.test', null)), isNull);
    expect(SetupCode.tryParse(jsonEncode({'v': 1, 'api': 'https://a.example.test'})), isNull);
  });

  test('text_that_is_not_json_or_not_an_object_is_refused', () {
    expect(SetupCode.tryParse('https://a.example.test'), isNull);
    expect(SetupCode.tryParse('hello'), isNull);
    expect(SetupCode.tryParse('[1, 2]'), isNull);
    expect(SetupCode.tryParse(''), isNull);
    expect(SetupCode.tryParse(null), isNull);
  });

  test('the_refusal_message', () {
    expect(SetupCode.notASetupCode, 'This is not a PVM Scanner setup code.');
  });
}
