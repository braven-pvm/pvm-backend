import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';

import '../api/device_api.dart';
import '../device/device_session.dart';
import '../device/staff.dart';
import '../queue/upload_queue.dart';
import '../scanner/document_scanner.dart';
import '../settings/app_settings.dart';
import 'staff_picker_screen.dart';

/// The scan flow. The camera opens as soon as this screen shows, and again after each
/// document, until the user closes the scanner. The list shows the documents of this batch
/// with their upload state. When the server has staff names, the app first asks who is
/// scanning, unless the remembered name is still on the list.
class BatchScreen extends StatefulWidget {
  const BatchScreen({
    super.key,
    required this.session,
    required this.queue,
    required this.scanner,
    required this.settings,
    required this.onOpenSettings,
  });

  final DeviceSession session;
  final UploadQueue queue;
  final DocumentScanner scanner;
  final AppSettings settings;
  final VoidCallback onOpenSettings;

  @override
  State<BatchScreen> createState() => _BatchScreenState();
}

class _BatchScreenState extends State<BatchScreen> {
  StreamSubscription<List<QueuedDocument>>? _subscription;
  List<QueuedDocument> _items = const [];
  List<DocumentType> _types = const [];
  String _typeCode = AppSettings.defaultDocumentType;
  StaffMember? _staff;
  var _scanning = false;

  @override
  void initState() {
    super.initState();
    _subscription = widget.queue.items.listen((items) => setState(() => _items = items));
    widget.session.addListener(_onConfigChanged);
    _loadTypes();
    WidgetsBinding.instance.addPostFrameCallback((_) => _scanLoop());
  }

  @override
  void dispose() {
    widget.session.removeListener(_onConfigChanged);
    _subscription?.cancel();
    super.dispose();
  }

  void _onConfigChanged() {
    if (mounted) {
      _loadTypes();
    }
  }

  /// The document types from the device config; the remembered type when it is still offered.
  Future<void> _loadTypes() async {
    final remembered = await widget.settings.documentType();
    final types = widget.session.config?.documentTypes ?? const <DocumentType>[];
    var code = remembered;
    if (types.isNotEmpty && !types.any((type) => type.code == code)) {
      code = types.any((type) => type.code == AppSettings.defaultDocumentType)
          ? AppSettings.defaultDocumentType
          : types.first.code;
      await widget.settings.setDocumentType(code);
    }
    final staffId = await widget.settings.staffId();
    final staff = StaffChoice.of(widget.session.config?.staff ?? const [], staffId).staff;
    if (mounted) {
      setState(() {
        _types = types;
        _typeCode = code;
        _staff = staff;
      });
    }
  }

  /// The device config for the staff question: the one in hand, or a short wait for the server
  /// on the very first start. Offline without any config, nobody is asked.
  Future<DeviceConfig?> _configForScan() async {
    final config = widget.session.config;
    if (config != null) {
      return config;
    }
    try {
      return await widget.session.refreshConfig().timeout(const Duration(seconds: 10));
    } on TimeoutException {
      return null;
    }
  }

  /// Makes sure a staff name is chosen when the server has names. [ask] shows the names even
  /// when one is remembered ("Change"). Returns false when the user went back without a name.
  Future<bool> _ensureStaff({bool ask = false}) async {
    final staffList = (await _configForScan())?.staff ?? const <StaffMember>[];
    final remembered = await widget.settings.staffId();
    final choice = StaffChoice.of(staffList, remembered);
    if (!mounted) {
      return false;
    }
    if (staffList.isEmpty || (!ask && !choice.mustAsk)) {
      setState(() => _staff = choice.staff);
      return true;
    }

    final chosen = await Navigator.of(context).push<StaffMember>(
      MaterialPageRoute(builder: (context) => StaffPickerScreen(staff: staffList, currentId: remembered)),
    );
    if (chosen == null) {
      return !choice.mustAsk;
    }
    await widget.settings.setStaffId(chosen.id);
    if (mounted) {
      setState(() => _staff = chosen);
    }
    return true;
  }

  Future<void> _changeStaff() async {
    if (!_scanning) {
      await _ensureStaff(ask: true);
    }
  }

  String _typeName(String code) {
    for (final type in _types) {
      if (type.code == code) {
        return type.displayName;
      }
    }
    return code == AppSettings.defaultDocumentType ? 'Proof of delivery' : code;
  }

  /// Opens the scanner, queues the document, and opens the scanner again, until the user
  /// closes the scanner.
  Future<void> _scanLoop() async {
    if (_scanning) {
      return;
    }
    setState(() => _scanning = true);
    try {
      if (!await _ensureStaff()) {
        return;
      }
      // The queue of this screen. On a revoke or reset the session holds it but keeps it open, so a
      // scan that ends after this screen is gone is still saved, with the type and name it was
      // taken under; it uploads after the next enrolment.
      final queue = widget.queue;
      while (mounted && widget.session.phase == SessionPhase.ready) {
        final typeCode = _typeCode;
        final staffId = _staff?.id;
        final document = await widget.scanner.scan();
        if (document == null) {
          break;
        }
        await queue.enqueue(document.pdf, typeCode, document.pageCount, staffId: staffId);
      }
    } on ScannerException catch (error) {
      await _showMessage('The scanner did not work', error.message);
    } on FileSystemException catch (error) {
      await _showMessage('The scan could not be saved', 'There may be no free space on this phone. (${error.message})');
    } finally {
      if (mounted) {
        setState(() => _scanning = false);
      }
    }
  }

  Future<void> _showMessage(String title, String message) async {
    if (!mounted) {
      return;
    }
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('OK'))],
      ),
    );
  }

  Future<void> _chooseType() async {
    final chosen = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            for (final type in _types)
              ListTile(
                minTileHeight: 64,
                title: Text(type.displayName, style: Theme.of(context).textTheme.titleLarge),
                trailing: type.code == _typeCode ? const Icon(Icons.check, size: 32) : null,
                onTap: () => Navigator.of(context).pop(type.code),
              ),
          ],
        ),
      ),
    );
    if (chosen != null && chosen != _typeCode) {
      await widget.settings.setDocumentType(chosen);
      setState(() => _typeCode = chosen);
    }
  }

  Future<void> _confirmRemove(QueuedDocument item) async {
    final remove = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Remove this document?'),
        content: const Text(
          'The scan is deleted from this phone and is not sent. '
          'Keep the paper so that you can scan it again.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Keep')),
          TextButton(onPressed: () => Navigator.of(context).pop(true), child: const Text('Remove')),
        ],
      ),
    );
    if (remove == true) {
      await widget.queue.remove(item.clientDocumentId);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('PVM Scanner'),
        actions: [
          IconButton(
            iconSize: 32,
            tooltip: 'Settings',
            onPressed: widget.onOpenSettings,
            icon: const Icon(Icons.settings_outlined),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _HeaderRow(
              label: 'Document type',
              value: _typeName(_typeCode),
              onChange: _types.length > 1 && !_scanning ? _chooseType : null,
            ),
            if ((widget.session.config?.staff ?? const <StaffMember>[]).isNotEmpty)
              _HeaderRow(
                label: 'Scanning',
                value: _staff?.name ?? 'Not chosen',
                onChange: _scanning ? null : _changeStaff,
              ),
            const Divider(height: 1),
            Expanded(
              child: _items.isEmpty
                  ? Center(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(
                          'The documents you scan show here.',
                          textAlign: TextAlign.center,
                          style: theme.textTheme.titleMedium,
                        ),
                      ),
                    )
                  : ListView.separated(
                      itemCount: _items.length,
                      separatorBuilder: (_, _) => const Divider(height: 1),
                      itemBuilder: (context, index) => _DocumentTile(
                        item: _items[index],
                        typeName: _typeName(_items[index].documentType),
                        onRemove: () => _confirmRemove(_items[index]),
                      ),
                    ),
            ),
            Padding(
              padding: const EdgeInsets.all(16),
              child: FilledButton.icon(
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(88)),
                onPressed: _scanning ? null : _scanLoop,
                icon: const Icon(Icons.document_scanner, size: 36),
                label: const Text('Scan next document', style: TextStyle(fontSize: 24)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _HeaderRow extends StatelessWidget {
  const _HeaderRow({required this.label, required this.value, required this.onChange});

  final String label;
  final String value;
  final VoidCallback? onChange;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 4),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: theme.textTheme.labelLarge),
                Text(value, style: theme.textTheme.titleLarge),
              ],
            ),
          ),
          if (onChange != null) TextButton(onPressed: onChange, child: const Text('Change')),
        ],
      ),
    );
  }
}

class _DocumentTile extends StatelessWidget {
  const _DocumentTile({required this.item, required this.typeName, required this.onRemove});

  final QueuedDocument item;
  final String typeName;
  final VoidCallback onRemove;

  static String _time(DateTime time) {
    final local = time.toLocal();
    String two(int value) => value.toString().padLeft(2, '0');
    return '${two(local.hour)}:${two(local.minute)}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pages = item.pageCount == 1 ? '1 page' : '${item.pageCount} pages';

    final (Widget icon, String label, Color color) = switch (item.state) {
      QueuedDocument.uploading => (
          const SizedBox.square(dimension: 32, child: CircularProgressIndicator(strokeWidth: 3)),
          'Uploading',
          theme.colorScheme.primary,
        ),
      QueuedDocument.received => (
          Icon(Icons.check_circle, size: 36, color: Colors.green.shade700),
          'Received',
          Colors.green.shade800,
        ),
      QueuedDocument.failed => (
          Icon(Icons.error, size: 36, color: theme.colorScheme.error),
          'Failed',
          theme.colorScheme.error,
        ),
      _ => (
          Icon(Icons.schedule, size: 36, color: theme.colorScheme.outline),
          item.attempts > 0 ? 'Waiting – will try again' : 'Waiting',
          theme.colorScheme.onSurfaceVariant,
        ),
    };

    final showReason = item.lastError != null &&
        (item.state == QueuedDocument.failed || (item.state == QueuedDocument.waiting && item.attempts > 0));

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 40, child: Center(child: icon)),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('$typeName · $pages', style: theme.textTheme.titleMedium),
                Text(
                  '${_time(item.createdAt)} · $label',
                  style: theme.textTheme.bodyLarge?.copyWith(color: color, fontWeight: FontWeight.w600),
                ),
                if (showReason)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(item.lastError!, style: theme.textTheme.bodyMedium),
                  ),
                if (item.note != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(item.note!, style: theme.textTheme.bodyMedium),
                  ),
                if (item.state == QueuedDocument.failed)
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: onRemove,
                      icon: const Icon(Icons.delete_outline),
                      label: const Text('Remove'),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
