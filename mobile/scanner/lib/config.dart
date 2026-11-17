/// Build-time settings, from `--dart-define` values. Nothing environment-specific is in the code.
class AppConfig {
  const AppConfig({
    required this.apiBaseUrl,
    required this.tenantId,
    required this.clientId,
    required this.apiScope,
  });

  /// Reads the `--dart-define` values given to `flutter build` or `flutter run`.
  factory AppConfig.fromEnvironment() => const AppConfig(
        apiBaseUrl: String.fromEnvironment('API_BASE_URL'),
        tenantId: String.fromEnvironment('ENTRA_TENANT_ID'),
        clientId: String.fromEnvironment('ENTRA_CLIENT_ID'),
        apiScope: String.fromEnvironment('ENTRA_API_SCOPE'),
      );

  /// The PVM API address, for example `https://api.example.com`.
  final String apiBaseUrl;

  /// The Microsoft Entra tenant (directory) ID of the workbench.
  final String tenantId;

  /// The client ID of the existing API app registration; the app is a public client of it.
  final String clientId;

  /// The API scope the workbench requests, for example `api://<api client id>/access_as_user`.
  final String apiScope;

  Uri get apiBaseUri => Uri.parse(apiBaseUrl);

  String get authority => 'https://login.microsoftonline.com/$tenantId';

  /// The names of the `--dart-define` values that are missing.
  List<String> get missing => [
        if (apiBaseUrl.isEmpty) 'API_BASE_URL',
        if (tenantId.isEmpty) 'ENTRA_TENANT_ID',
        if (clientId.isEmpty) 'ENTRA_CLIENT_ID',
        if (apiScope.isEmpty) 'ENTRA_API_SCOPE',
      ];
}
