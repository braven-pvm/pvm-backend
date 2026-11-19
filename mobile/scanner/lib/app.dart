import 'dart:async';

import 'package:flutter/material.dart';

import 'device/device_session.dart';
import 'scanner/document_scanner.dart';
import 'screens/home_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/setup_screen.dart';
import 'settings/app_settings.dart';

/// The app's long-lived services.
class AppServices {
  const AppServices({required this.session, required this.scanner, required this.settings});

  final DeviceSession session;
  final DocumentScanner scanner;
  final AppSettings settings;
}

class ScannerApp extends StatelessWidget {
  const ScannerApp({super.key, required this.services});

  final AppServices services;

  @override
  Widget build(BuildContext context) {
    final scheme = ColorScheme.fromSeed(seedColor: const Color(0xFF1F5FA8));
    // Sizes (englishLike) and colours (black) together: the colour-only Typography.black has no
    // sizes, so scaling it alone fails an assertion in debug builds and does nothing in release.
    final typography = Typography.material2021();
    final textTheme = typography.englishLike.merge(typography.black).apply(fontSizeFactor: 1.15);
    return MaterialApp(
      title: 'PVM Scanner',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: scheme,
        visualDensity: VisualDensity.comfortable,
        textTheme: textTheme,
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

/// Chooses the screen: setup (no device key) or the start screen.
class AppShell extends StatefulWidget {
  const AppShell({super.key, required this.services});

  final AppServices services;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> with WidgetsBindingObserver {
  SessionPhase? _shownPhase = SessionPhase.starting;

  DeviceSession get _session => widget.services.session;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _session.addListener(_onSessionChanged);
    // After the first frame: the session's notifications rebuild this widget and its navigator.
    WidgetsBinding.instance.addPostFrameCallback((_) => _session.start());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _session.removeListener(_onSessionChanged);
    _session.close();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _session.queue?.retryNow();
    }
  }

  void _onSessionChanged() {
    final phase = _session.phase;
    if (phase == _shownPhase) {
      setState(() {});
      return;
    }
    _shownPhase = phase;
    // A screen opened on top (settings, the QR camera) belongs to the old phase.
    if (mounted) {
      Navigator.of(context).popUntil((route) => route.isFirst);
    }
    if (phase == SessionPhase.ready) {
      unawaited(_session.refreshConfig());
    }
    setState(() {});
  }

  void _openSettings() {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (context) => SettingsScreen(
          session: _session,
          onReset: _session.reset,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    switch (_session.phase) {
      case SessionPhase.starting:
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      case SessionPhase.startFailed:
        return _StartFailedScreen(error: _session.startError ?? '', onTryAgain: _session.start);
      case SessionPhase.needsSetup:
        return SetupScreen(session: _session, message: _session.setupMessage);
      case SessionPhase.ready:
        return HomeScreen(
          // A new enrolment opens a new queue: start the screen afresh.
          key: ObjectKey(_session.queue),
          session: _session,
          queue: _session.queue!,
          scanner: widget.services.scanner,
          settings: widget.services.settings,
          onOpenSettings: _openSettings,
        );
    }
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
