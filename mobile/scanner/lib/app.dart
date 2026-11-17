import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';

import 'api/documents_api.dart';
import 'auth/auth_service.dart';
import 'config.dart';
import 'queue/upload_queue.dart';
import 'scanner/document_scanner.dart';
import 'screens/batch_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/sign_in_screen.dart';
import 'settings/app_settings.dart';

/// The app's long-lived services.
class AppServices {
  const AppServices({
    required this.config,
    required this.auth,
    required this.scanner,
    required this.settings,
    required this.api,
  });

  final AppConfig config;
  final AuthService auth;
  final DocumentScanner scanner;
  final AppSettings settings;

  /// Null when the build has no backend settings.
  final DocumentsApi? api;
}

class ScannerApp extends StatelessWidget {
  const ScannerApp({super.key, required this.services});

  final AppServices services;

  @override
  Widget build(BuildContext context) {
    final scheme = ColorScheme.fromSeed(seedColor: const Color(0xFF1F5FA8));
    return MaterialApp(
      title: 'PVM Scanner',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: scheme,
        visualDensity: VisualDensity.comfortable,
        textTheme: Typography.material2021().black.apply(fontSizeFactor: 1.15),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(64),
            textStyle: const TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            minimumSize: const Size.fromHeight(56),
            textStyle: const TextStyle(fontSize: 18),
          ),
        ),
        textButtonTheme: TextButtonThemeData(
          style: TextButton.styleFrom(
            minimumSize: const Size(64, 48),
            textStyle: const TextStyle(fontSize: 18),
          ),
        ),
      ),
      home: AppShell(services: services),
    );
  }
}

enum _Phase { starting, notConfigured, startFailed, signedOut, signedIn }

/// Chooses the screen: sign-in, or the batch screen once a user is signed in.
class AppShell extends StatefulWidget {
  const AppShell({super.key, required this.services});

  final AppServices services;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> with WidgetsBindingObserver {
  var _phase = _Phase.starting;
  UploadQueue? _queue;
  String? _signInError;
  String? _startError;
  var _signingIn = false;

  AppServices get _services => widget.services;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _queue?.close();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _queue?.retryNow();
    }
  }

  Future<void> _start() async {
    setState(() {
      _phase = _Phase.starting;
      _startError = null;
    });
    if (_services.config.missing.isNotEmpty) {
      setState(() => _phase = _Phase.notConfigured);
      return;
    }

    try {
      SignedInUser? user;
      try {
        user = await _services.auth.restore();
      } on SignInException catch (error) {
        _signInError = error.message;
      }

      if (user == null) {
        setState(() => _phase = _Phase.signedOut);
        return;
      }

      await _openQueue();
      if (mounted) {
        setState(() => _phase = _Phase.signedIn);
      }
    } on Object catch (error) {
      _showStartFailure(error);
    }
  }

  void _showStartFailure(Object error) {
    if (mounted) {
      setState(() {
        _phase = _Phase.startFailed;
        _startError = '$error';
      });
    }
  }

  Future<void> _openQueue() async {
    var queue = _queue;
    if (queue == null) {
      final documents = await getApplicationDocumentsDirectory();
      queue = await UploadQueue.open(
        directory: Directory('${documents.path}/upload_queue'),
        uploader: _services.api!,
      );
      _queue = queue;
    }
    queue.start();
    queue.resume();
  }

  /// Interactive sign-in; returns true when it worked.
  Future<bool> _signIn() async {
    setState(() {
      _signingIn = true;
      _signInError = null;
    });
    try {
      await _services.auth.signIn();
      await _openQueue();
      if (mounted) {
        setState(() => _phase = _Phase.signedIn);
      }
      return true;
    } on SignInException catch (error) {
      if (mounted && !error.cancelled) {
        setState(() => _signInError = error.message);
      }
      return false;
    } on Object catch (error) {
      _showStartFailure(error);
      return false;
    } finally {
      if (mounted) {
        setState(() => _signingIn = false);
      }
    }
  }

  Future<void> _signOut() async {
    await _queue?.close();
    _queue = null;
    await _services.auth.signOut();
    if (mounted) {
      setState(() => _phase = _Phase.signedOut);
    }
  }

  void _openSettings(BuildContext context) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (context) => SettingsScreen(
          config: _services.config,
          auth: _services.auth,
          queue: _queue!,
          onSignOut: () async {
            Navigator.of(context).pop();
            await _signOut();
          },
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    switch (_phase) {
      case _Phase.starting:
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      case _Phase.notConfigured:
        return _NotConfiguredScreen(missing: _services.config.missing);
      case _Phase.startFailed:
        return _StartFailedScreen(error: _startError ?? '', onTryAgain: _start);
      case _Phase.signedOut:
        return SignInScreen(
          busy: _signingIn,
          error: _signInError,
          redirectUri: _services.auth.redirectUri,
          onSignIn: _signIn,
        );
      case _Phase.signedIn:
        return BatchScreen(
          queue: _queue!,
          scanner: _services.scanner,
          api: _services.api!,
          settings: _services.settings,
          onSignInAgain: _signIn,
          onOpenSettings: () => _openSettings(context),
        );
    }
  }
}

class _NotConfiguredScreen extends StatelessWidget {
  const _NotConfiguredScreen({required this.missing});

  final List<String> missing;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('PVM Scanner')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Text(
          'This copy of the app was built without its server settings. '
          'Ask IT for a new copy.\n\nMissing: ${missing.join(', ')}',
          style: Theme.of(context).textTheme.titleMedium,
        ),
      ),
    );
  }
}

class _StartFailedScreen extends StatelessWidget {
  const _StartFailedScreen({required this.error, required this.onTryAgain});

  final String error;
  final VoidCallback onTryAgain;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('PVM Scanner')),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('The app could not start.', style: theme.textTheme.headlineSmall),
              const SizedBox(height: 12),
              Text(
                'Your scans are kept on this phone. Try again; if this keeps happening, tell IT.',
                style: theme.textTheme.titleMedium,
              ),
              const SizedBox(height: 12),
              SelectableText(error, style: theme.textTheme.bodySmall),
              const Spacer(),
              FilledButton.icon(
                onPressed: onTryAgain,
                icon: const Icon(Icons.refresh, size: 28),
                label: const Text('Try again'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
