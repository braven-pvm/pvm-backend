import 'package:flutter/material.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../auth/auth_service.dart';
import '../config.dart';
import '../queue/upload_queue.dart';

/// Signed-in user, app version, backend address, and sign out.
class SettingsScreen extends StatelessWidget {
  const SettingsScreen({
    super.key,
    required this.config,
    required this.auth,
    required this.queue,
    required this.onSignOut,
  });

  final AppConfig config;
  final AuthService auth;
  final UploadQueue queue;
  final Future<void> Function() onSignOut;

  Future<void> _confirmSignOut(BuildContext context) async {
    final unsent = queue.current.where((item) => item.state != QueuedDocument.received).length;
    final message = unsent == 0
        ? 'You must sign in again before the next scan.'
        : '$unsent ${unsent == 1 ? 'document is' : 'documents are'} not sent yet. '
            'They stay on this phone and are sent after the next sign-in.';
    final signOut = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Sign out?'),
        content: Text(message),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.of(context).pop(true), child: const Text('Sign out')),
        ],
      ),
    );
    if (signOut == true) {
      await onSignOut();
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final user = auth.user;
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: SafeArea(
        child: ListView(
          children: [
            ListTile(
              leading: const Icon(Icons.person_outline, size: 32),
              title: const Text('Signed in as'),
              subtitle: Text(
                [user?.name, user?.username].whereType<String>().toSet().join('\n'),
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
            ListTile(
              leading: const Icon(Icons.cloud_outlined, size: 32),
              title: const Text('Server'),
              subtitle: SelectableText(config.apiBaseUrl, style: theme.textTheme.titleMedium),
            ),
            if (auth.redirectUri != null)
              ListTile(
                leading: const Icon(Icons.key_outlined, size: 32),
                title: const Text('Sign-in redirect URI (for IT)'),
                subtitle: SelectableText(auth.redirectUri!),
              ),
            Padding(
              padding: const EdgeInsets.all(16),
              child: OutlinedButton.icon(
                onPressed: () => _confirmSignOut(context),
                icon: const Icon(Icons.logout),
                label: const Text('Sign out'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
