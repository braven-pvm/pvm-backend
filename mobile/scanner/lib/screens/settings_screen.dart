import 'package:flutter/material.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../device/device_session.dart';

/// Device name, server address, app version, and "Reset this phone".
class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key, required this.session, required this.onReset});

  final DeviceSession session;
  final Future<void> Function() onReset;

  Future<void> _confirmReset(BuildContext context) async {
    final reset = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Reset this phone?'),
        content: const Text(
          'This phone stops scanning until an admin gives a new setup code. '
          'Scans not yet sent stay on the phone.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.of(context).pop(true), child: const Text('Reset')),
        ],
      ),
    );
    if (reset == true) {
      await onReset();
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: SafeArea(
        child: ListenableBuilder(
          listenable: session,
          builder: (context, _) {
            final credentials = session.credentials;
            return ListView(
              children: [
                ListTile(
                  leading: const Icon(Icons.smartphone, size: 32),
                  title: const Text('Device name'),
                  subtitle: Text(
                    (credentials?.deviceName ?? '').isEmpty ? '–' : credentials!.deviceName,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                ListTile(
                  leading: const Icon(Icons.cloud_outlined, size: 32),
                  title: const Text('Server'),
                  subtitle: SelectableText(
                    credentials?.apiBaseUri.toString() ?? '–',
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                ListTile(
                  leading: const Icon(Icons.info_outline, size: 32),
                  title: const Text('App version'),
                  subtitle: FutureBuilder<PackageInfo>(
                    future: PackageInfo.fromPlatform(),
                    builder: (context, snapshot) => Text(
                      snapshot.hasData ? '${snapshot.data!.version} (${snapshot.data!.buildNumber})' : '…',
                      style: theme.textTheme.titleMedium,
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.all(16),
                  child: OutlinedButton.icon(
                    onPressed: () => _confirmReset(context),
                    icon: const Icon(Icons.restart_alt),
                    label: const Text('Reset this phone'),
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}
