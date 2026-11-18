import 'package:flutter/material.dart';

import '../api/device_api.dart';
import '../device/device_session.dart';
import '../device/setup_code.dart';
import 'qr_scan_screen.dart';

/// First start, or after the phone was removed or reset: one large button to scan the setup
/// QR code an Admin shows in the workbench (Documents > Scanner devices).
class SetupScreen extends StatefulWidget {
  const SetupScreen({super.key, required this.session, this.message});

  final DeviceSession session;

  /// For example "This phone was removed. ..."; null on a first start.
  final String? message;

  @override
  State<SetupScreen> createState() => _SetupScreenState();
}

class _SetupScreenState extends State<SetupScreen> {
  String? _error;
  var _busy = false;

  Future<void> _scan() async {
    final raw = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (context) => const QrScanScreen()),
    );
    if (!mounted || raw == null) {
      return;
    }

    final code = SetupCode.tryParse(raw);
    if (code == null) {
      setState(() => _error = SetupCode.notASetupCode);
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    final outcome = await widget.session.enrol(code);
    if (!mounted) {
      return;
    }
    setState(() {
      _busy = false;
      if (outcome is EnrolRefused) {
        _error = outcome.message;
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final notice = _error ?? widget.message;
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Spacer(),
              Icon(Icons.document_scanner_outlined, size: 96, color: theme.colorScheme.primary),
              const SizedBox(height: 16),
              Text('PVM Scanner', textAlign: TextAlign.center, style: theme.textTheme.headlineMedium),
              const SizedBox(height: 8),
              Text(
                'Ask an admin for the setup code of this phone, then scan it.',
                textAlign: TextAlign.center,
                style: theme.textTheme.titleMedium,
              ),
              const Spacer(),
              if (notice != null) ...[
                Text(
                  notice,
                  style: theme.textTheme.titleMedium?.copyWith(color: theme.colorScheme.error),
                ),
                const SizedBox(height: 16),
              ],
              FilledButton.icon(
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(88)),
                onPressed: _busy ? null : _scan,
                icon: _busy
                    ? const SizedBox.square(dimension: 28, child: CircularProgressIndicator(strokeWidth: 3))
                    : const Icon(Icons.qr_code_scanner, size: 36),
                label: const Text('Scan setup code', style: TextStyle(fontSize: 24)),
              ),
              const SizedBox(height: 24),
            ],
          ),
        ),
      ),
    );
  }
}
