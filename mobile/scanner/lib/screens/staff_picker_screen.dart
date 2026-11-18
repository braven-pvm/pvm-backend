import 'package:flutter/material.dart';

import '../api/device_api.dart';

/// "Who is scanning?": the active staff names as large buttons. Closes with the chosen name,
/// or null when the user goes back.
class StaffPickerScreen extends StatelessWidget {
  const StaffPickerScreen({super.key, required this.staff, this.currentId});

  final List<StaffMember> staff;
  final String? currentId;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Who is scanning?')),
      body: SafeArea(
        child: ListView.separated(
          padding: const EdgeInsets.all(16),
          itemCount: staff.length,
          separatorBuilder: (_, _) => const SizedBox(height: 12),
          itemBuilder: (context, index) {
            final member = staff[index];
            final selected = member.id == currentId;
            final style = FilledButton.styleFrom(
              minimumSize: const Size.fromHeight(72),
              textStyle: theme.textTheme.headlineSmall,
            );
            return selected
                ? FilledButton.icon(
                    style: style,
                    onPressed: () => Navigator.of(context).pop(member),
                    icon: const Icon(Icons.check, size: 32),
                    label: Text(member.name),
                  )
                : FilledButton.tonal(
                    style: style,
                    onPressed: () => Navigator.of(context).pop(member),
                    child: Text(member.name),
                  );
          },
        ),
      ),
    );
  }
}
