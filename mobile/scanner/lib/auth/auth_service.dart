import 'package:flutter/services.dart';
import 'package:msal_auth/msal_auth.dart';

import '../api/documents_api.dart';
import '../config.dart';

/// The signed-in Microsoft account.
class SignedInUser {
  const SignedInUser({required this.name, required this.username});

  final String? name;
  final String? username;

  String get displayName => name ?? username ?? 'Signed in';
}

/// A sign-in that did not complete, with a message for the user.
class SignInException implements Exception {
  const SignInException(this.message, {this.cancelled = false});

  final String message;
  final bool cancelled;

  @override
  String toString() => message;
}

/// Microsoft Entra sign-in for the app, and the bearer token for the API.
abstract interface class AuthService implements AccessTokenSource {
  /// The Android redirect URI of this build (`msauth://<package>/<signature hash>`).
  String? get redirectUri;

  SignedInUser? get user;

  /// Restores the remembered sign-in without any screen. Null when the user must sign in.
  Future<SignedInUser?> restore();

  /// Shows the Microsoft sign-in page. Throws [SignInException].
  Future<SignedInUser> signIn();

  Future<void> signOut();
}

/// [AuthService] with `msal_auth` (MSAL for Android): a public client of the API app
/// registration, single account mode, sign-in in the browser (no broker app needed).
class MsalAuthService implements AuthService {
  MsalAuthService(this._config);

  static const _appChannel = MethodChannel('za.co.pvm.scanner/app');

  final AppConfig _config;
  SingleAccountPca? _pca;
  SignedInUser? _user;
  String? _redirectUri;

  @override
  String? get redirectUri => _redirectUri;

  @override
  SignedInUser? get user => _user;

  List<String> get _scopes => [_config.apiScope];

  Future<SingleAccountPca> _client() async {
    final existing = _pca;
    if (existing != null) {
      return existing;
    }

    // The redirect URI holds the hash of the key that signed this APK, so the native side
    // computes it; debug and release builds then need no code change.
    try {
      final redirectUri = await _appChannel.invokeMethod<String>('redirectUri');
      _redirectUri = redirectUri;
      return _pca = await SingleAccountPca.create(
        clientId: _config.clientId,
        androidConfig: AndroidConfig(
          configFilePath: 'assets/msal_config.json',
          redirectUri: redirectUri!,
        ),
      );
    } on MsalException catch (error) {
      throw SignInException('The sign-in could not start: ${error.message}');
    } on PlatformException catch (error) {
      throw SignInException('The sign-in could not start: ${error.message}');
    }
  }

  @override
  Future<SignedInUser?> restore() async {
    final pca = await _client();
    final Account account;
    try {
      account = await pca.currentAccount;
    } on MsalException {
      return null; // No remembered account.
    }

    try {
      final result = await pca.acquireTokenSilent(scopes: _scopes);
      return _user = _userOf(result.account);
    } on MsalUiRequiredException {
      return null; // The remembered sign-in has expired: the user must sign in.
    } on MsalException {
      // Probably no network. The account is known, so scanning can go on; the queue uploads
      // once a token can be renewed.
      return _user = _userOf(account);
    }
  }

  @override
  Future<SignedInUser> signIn() async {
    final pca = await _client();
    try {
      final result = await pca.acquireToken(
        scopes: _scopes,
        prompt: Prompt.selectAccount,
        authority: _config.authority,
      );
      return _user = _userOf(result.account);
    } on MsalUserCancelException {
      throw const SignInException('The sign-in was cancelled.', cancelled: true);
    } on MsalException catch (error) {
      throw SignInException('The sign-in did not work: ${error.message}');
    }
  }

  @override
  Future<String?> accessToken() async {
    final pca = await _client();
    try {
      final result = await pca.acquireTokenSilent(scopes: _scopes);
      return result.accessToken;
    } on MsalUiRequiredException {
      return null;
    } on MsalNoCurrentAccountException {
      return null;
    }
    // Any other MsalException (for example no network) goes to the caller, which retries later.
  }

  @override
  Future<void> signOut() async {
    final pca = await _client();
    try {
      await pca.signOut();
    } on MsalException {
      // Already signed out.
    }
    _user = null;
  }

  static SignedInUser _userOf(Account account) => SignedInUser(name: account.name, username: account.username);
}
