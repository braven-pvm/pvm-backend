import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../api/device_api.dart';
import '../device/device_session.dart';
import '../device/staff.dart';
import '../queue/upload_queue.dart';
import '../scanner/document_scanner.dart';
import '../scanner/thumbnail.dart';
import '../settings/app_settings.dart';
import 'review_screen.dart';
import 'staff_picker_screen.dart';

/// The start screen (design 6.12): who is scanning, one large button per document type, and the
/// recent scans with their state. A type opens the scanner; after each scan the review screen
/// asks for Send, and Send opens the scanner again for the same type. With only one type the
/// scanner opens at once, as before.
class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    required this.session,
    required this.queue,
    required this.scanner,
    required this.settings,
    required this.onOpenSettings,
    this.thumbnailer = thumbnailOfFile,
  });

  final DeviceSession session;
  final UploadQueue queue;
  final DocumentScanner scanner;
  final AppSettings settings;
  final VoidCallback onOpenSettings;

  /// Makes the upload thumbnail from the first page image.
  final Future<Uint8List?> Function(File? firstPage) thumbnailer;

  static const emptyText = 'No scans yet. Choose a document type to start.';

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  static const _fallbackTypes = [DocumentType(code: 'POD', displayName: 'Proof of delivery')];

  StreamSubscription<List<QueuedDocument>>? _subscription;
  List<QueuedDocument> _items = const [];
  final _states = <String, String>{};
  final _justSent = <String>{};
  StaffMember? _staff;
  var _scanning = false;

  @override
  void initState() {
    super.initState();
    _subscription = widget.queue.items.listen(_onItems);
    widget.session.addListener(_onConfigChanged);
    WidgetsBinding.instance.addPostFrameCallback((_) => _start());
  }

  @override
  void dispose() {
    widget.session.removeListener(_onConfigChanged);
    _subscription?.cancel();
    super.dispose();
  }

  List<DocumentType> get _types {
    final types = widget.session.config?.documentTypes ?? const <DocumentType>[];
    return types.isEmpty ? _fallbackTypes : types;
  }

  List<StaffMember> get _staffList => widget.session.config?.staff ?? const <StaffMember>[];

  void _onItems(List<QueuedDocument> items) {
    var sentNow = false;
    for (final item in items) {
      final before = _states[item.clientDocumentId];
      if (item.state == QueuedDocument.received && before != null && before != QueuedDocument.received) {
        _justSent.add(item.clientDocumentId);
        sentNow = true;
      }
      _states[item.clientDocumentId] = item.state;
    }
    if (sentNow) {
      unawaited(HapticFeedback.mediumImpact());
    }
    setState(() => _items = items);
  }

  void _onConfigChanged() {
    if (mounted) {
      _loadStaff();
    }
  }

  Future<void> _loadStaff() async {
    final staffId = await widget.settings.staffId();
    final staff = StaffChoice.of(_staffList, staffId).staff;
    if (mounted) {
      setState(() => _staff = staff);
    }
  }

  /// With one document type the scanner opens at once.
  Future<void> _start() async {
    await _loadStaff();
    final config = await _configForScan();
    if (!mounted) {
      return;
    }
    final types = config == null || config.documentTypes.isEmpty ? _fallbackTypes : config.documentTypes;
    if (types.length == 1) {
      await _scanFlow(types.single);
    }
  }

  /// The device config: the one in hand, or a short wait for the server on the very first start.
  /// Offline without any config, nobody is asked and only proof of delivery is offered.
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
    return switch (code) {
      'POD' => 'Proof of delivery',
      QueuedDocument.generalType => 'General document',
      _ => code,
    };
  }

  bool get _ready => mounted && widget.session.phase == SessionPhase.ready;

  /// Scanner, review, Send, and the scanner again, until the person closes the scanner or
  /// chooses "Send and finish".
  Future<void> _scanFlow(DocumentType type) async {
    if (_scanning) {
      return;
    }
    setState(() => _scanning = true);
    try {
      if (!await _ensureStaff()) {
        return;
      }
      await widget.settings.setDocumentType(type.code);
      // The queue of this screen. On a revoke or reset the session holds it but keeps it open, so
      // a scan that ends after this screen is gone is still saved, with the type and name it was
      // taken under; it uploads after the next enrolment.
      final queue = widget.queue;
      while (_ready) {
        final staffId = _staff?.id;
        final document = await widget.scanner.scan();
        if (document == null) {
          break;
        }
        final thumbnail = widget.thumbnailer(document.firstPage);

        if (!_ready || !mounted) {
          // The phone was removed or reset while the camera was open: keep the scan as it is.
          await _keep(queue, document, type.code, staffId, thumbnail: await thumbnail);
          break;
        }

        if (!context.mounted) {
          break;
        }
        final result = await Navigator.of(context).push<ReviewResult>(
          MaterialPageRoute(
            builder: (context) => ReviewScreen(
              document: document,
              typeName: type.displayName,
              withTitle: type.code == QueuedDocument.generalType,
            ),
          ),
        );

        switch (result?.action) {
          case ReviewAction.scanAgain:
            await document.discard();
          case ReviewAction.discard:
            await document.discard();
            return;
          case ReviewAction.send:
            await _keep(queue, document, type.code, staffId,
                title: result!.title, note: result.note, thumbnail: await thumbnail);
          case ReviewAction.finish:
            await _keep(queue, document, type.code, staffId,
                title: result!.title, note: result.note, thumbnail: await thumbnail);
            return;
          case null:
            // The review screen was closed by the app (the phone was removed): never lose a scan.
            await _keep(queue, document, type.code, staffId, thumbnail: await thumbnail);
            return;
        }
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

  Future<void> _keep(
    UploadQueue queue,
    ScannedDocument document,
    String typeCode,
    String? staffId, {
    String? title,
    String? note,
    Uint8List? thumbnail,
  }) async {
    await queue.enqueue(
      document.pdf,
      typeCode,
      document.pageCount,
      staffId: staffId,
      title: title,
      note: note,
      thumbnail: thumbnail,
    );
    await document.deleteImages();
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

  Future<void> _confirmRemove(QueuedDocument item) async {
    final remove = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Remove this scan?'),
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

  static IconData _iconOf(String code) => switch (code) {
        'POD' => Icons.local_shipping_outlined,
        QueuedDocument.generalType => Icons.description_outlined,
        _ => Icons.document_scanner_outlined,
      };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final showStaff = _staffList.isNotEmpty;

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
        child: ListView(
          padding: const EdgeInsets.only(bottom: 24),
          children: [
            if (showStaff)
              _StaffRow(name: _staff?.name ?? 'Not chosen', onChange: _scanning ? null : _changeStaff),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final type in _types) ...[
                    FilledButton.icon(
                      style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(88),
                        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
                        alignment: Alignment.centerLeft,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                      ),
                      onPressed: _scanning ? null : () => _scanFlow(type),
                      icon: Icon(_iconOf(type.code), size: 36),
                      label: Text(type.displayName, style: const TextStyle(fontSize: 24)),
                    ),
                    const SizedBox(height: 12),
                  ],
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
              child: Text('Recent scans', style: theme.textTheme.titleLarge),
            ),
            const Divider(height: 1),
            if (_items.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 40),
                child: Column(
                  children: [
                    Icon(Icons.inbox_outlined, size: 56, color: theme.colorScheme.outline),
                    const SizedBox(height: 12),
                    Text(HomeScreen.emptyText, textAlign: TextAlign.center, style: theme.textTheme.titleMedium),
                  ],
                ),
              )
            else
              for (final item in _items) ...[
                _ScanTile(
                  key: ValueKey(item.clientDocumentId),
                  item: item,
                  thumbnail: widget.queue.thumbnailOf(item),
                  typeName: _typeName(item.documentType),
                  justSent: _justSent.contains(item.clientDocumentId),
                  onTryAgain: () => widget.queue.retry(item.clientDocumentId),
                  onRemove: () => _confirmRemove(item),
                ),
                const Divider(height: 1),
              ],
          ],
        ),
      ),
    );
  }
}

class _StaffRow extends StatelessWidget {
  const _StaffRow({required this.name, required this.onChange});

  final String name;
  final VoidCallback? onChange;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: theme.colorScheme.surfaceContainerLow,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        child: Row(
          children: [
            Icon(Icons.person_outline, size: 32, color: theme.colorScheme.primary),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Scanning', style: theme.textTheme.labelLarge),
                  Text(name, style: theme.textTheme.titleLarge),
                ],
              ),
            ),
            TextButton(onPressed: onChange, child: const Text('Change')),
          ],
        ),
      ),
    );
  }
}

class _ScanTile extends StatelessWidget {
  const _ScanTile({
    super.key,
    required this.item,
    required this.thumbnail,
    required this.typeName,
    required this.justSent,
    required this.onTryAgain,
    required this.onRemove,
  });

  final QueuedDocument item;
  final File? thumbnail;
  final String typeName;
  final bool justSent;
  final VoidCallback onTryAgain;
  final VoidCallback onRemove;

  static String _time(DateTime time) {
    final local = time.toLocal();
    final now = DateTime.now();
    String two(int value) => value.toString().padLeft(2, '0');
    final clock = '${two(local.hour)}:${two(local.minute)}';
    if (local.year == now.year && local.month == now.month && local.day == now.day) {
      return clock;
    }
    return '${local.day}/${local.month} $clock';
  }

  String get _title {
    if (item.title != null) {
      return item.title!;
    }
    return item.documentType == QueuedDocument.generalType ? 'Untitled' : typeName;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pages = item.pageCount == 1 ? '1 page' : '${item.pageCount} pages';
    final title = _title;
    final details = [if (title != typeName) typeName, pages, _time(item.createdAt)].join(' · ');
    final sentColor = Colors.green.shade800;

    final (Widget icon, String label, Color color) = switch (item.state) {
      QueuedDocument.uploading => (
          const SizedBox.square(dimension: 24, child: CircularProgressIndicator(strokeWidth: 3)),
          'Sending',
          theme.colorScheme.primary,
        ),
      QueuedDocument.received => (
          _SentCheck(animate: justSent, color: sentColor),
          'Sent',
          sentColor,
        ),
      QueuedDocument.failed => (
          Icon(Icons.error, size: 28, color: theme.colorScheme.error),
          'Problem',
          theme.colorScheme.error,
        ),
      _ => (
          Icon(Icons.schedule, size: 28, color: theme.colorScheme.outline),
          'Waiting',
          theme.colorScheme.onSurfaceVariant,
        ),
    };

    final failed = item.state == QueuedDocument.failed;
    final retrying = item.state == QueuedDocument.waiting && item.attempts > 0;
    final reason = failed || retrying ? item.lastError : null;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _Thumbnail(file: thumbnail),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
                ),
                Text(details, style: theme.textTheme.bodyMedium),
                const SizedBox(height: 4),
                Row(
                  children: [
                    SizedBox(width: 32, child: Center(child: icon)),
                    const SizedBox(width: 6),
                    Flexible(
                      child: Text(
                        retrying ? 'Waiting – will try again' : label,
                        style: theme.textTheme.titleMedium?.copyWith(color: color, fontWeight: FontWeight.w600),
                      ),
                    ),
                  ],
                ),
                if (reason != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(reason, style: theme.textTheme.bodyMedium),
                  ),
                if (item.note != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(item.note!, style: theme.textTheme.bodyMedium),
                  ),
                if (failed || retrying)
                  Wrap(
                    spacing: 8,
                    children: [
                      TextButton.icon(
                        onPressed: onTryAgain,
                        icon: const Icon(Icons.refresh),
                        label: const Text('Try again'),
                      ),
                      if (failed)
                        TextButton.icon(
                          onPressed: onRemove,
                          icon: const Icon(Icons.delete_outline),
                          label: const Text('Remove'),
                        ),
                    ],
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Thumbnail extends StatelessWidget {
  const _Thumbnail({required this.file});

  final File? file;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final placeholder = Icon(Icons.description_outlined, size: 32, color: theme.colorScheme.outline);
    return ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: Container(
        width: 56,
        height: 72,
        color: theme.colorScheme.surfaceContainerHighest,
        alignment: Alignment.center,
        child: file == null
            ? placeholder
            : Image.file(
                file!,
                width: 56,
                height: 72,
                fit: BoxFit.cover,
                cacheWidth: 168,
                errorBuilder: (_, _, _) => placeholder,
              ),
      ),
    );
  }
}

/// The check mark of a sent scan; it pops in when the scan was sent just now.
class _SentCheck extends StatelessWidget {
  const _SentCheck({required this.animate, required this.color});

  final bool animate;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final check = Icon(Icons.check_circle, size: 28, color: color);
    if (!animate) {
      return check;
    }
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: const Duration(milliseconds: 600),
      curve: Curves.elasticOut,
      builder: (context, value, child) => Transform.scale(scale: value, child: child),
      child: check,
    );
  }
}
