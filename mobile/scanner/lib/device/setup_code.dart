import 'dart:convert';

/// The content of the setup QR code the workbench shows (design 6.10):
/// `{"v":1,"api":"<https API base URL>","code":"<setup code>"}`.
class SetupCode {
  const SetupCode({required this.api, required this.code});

  /// The message for anything that is not a setup code.
  static const notASetupCode = 'This is not a PVM Scanner setup code.';

  /// The PVM API base address; always `https`.
  final Uri api;

  /// The one-time setup code.
  final String code;

  /// The setup code in [raw], or null when [raw] is not a version 1 setup code with an `https`
  /// API address (no user info, query or fragment) and a code.
  static SetupCode? tryParse(String? raw) {
    if (raw == null || raw.isEmpty) {
      return null;
    }

    final Object? json;
    try {
      json = jsonDecode(raw);
    } on FormatException {
      return null;
    }
    if (json is! Map<String, dynamic>) {
      return null;
    }

    final version = json['v'];
    final api = json['api'];
    final code = json['code'];
    if (version is! int || version != 1 || api is! String || code is! String || code.trim().isEmpty) {
      return null;
    }

    final uri = Uri.tryParse(api);
    if (uri == null ||
        uri.scheme != 'https' ||
        uri.host.isEmpty ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment) {
      return null;
    }

    return SetupCode(api: uri, code: code.trim());
  }
}
