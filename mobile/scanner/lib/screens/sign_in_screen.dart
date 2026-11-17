import 'package:flutter/material.dart';

/// The first screen: one large button to sign in with Microsoft.
class SignInScreen extends StatelessWidget {
  const SignInScreen({
    super.key,
    required this.busy,
    required this.error,
    required this.redirectUri,
    required this.onSignIn,
  });

  final bool busy;
  final String? error;
  final String? redirectUri;
  final Future<bool> Function() onSignIn;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
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
                'Sign in with your work Microsoft account.',
                textAlign: TextAlign.center,
                style: theme.textTheme.titleMedium,
              ),
              const Spacer(),
              if (error != null) ...[
                Text(error!, style: theme.textTheme.bodyLarge?.copyWith(color: theme.colorScheme.error)),
                if (redirectUri != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: SelectableText(
                      'For IT: redirect URI $redirectUri',
                      style: theme.textTheme.bodySmall,
                    ),
                  ),
                const SizedBox(height: 16),
              ],
              FilledButton.icon(
                onPressed: busy ? null : onSignIn,
                icon: busy
                    ? const SizedBox.square(dimension: 24, child: CircularProgressIndicator(strokeWidth: 3))
                    : const Icon(Icons.login, size: 28),
                label: const Text('Sign in'),
              ),
              const SizedBox(height: 24),
            ],
          ),
        ),
      ),
    );
  }
}
