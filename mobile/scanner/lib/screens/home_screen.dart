import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../api/device_api.dart';
import '../device/device_session.dart';
import '../device/staff.dart';
import '../device/vibration.dart';
import '../queue/upload_queue.dart';
import '../scanner/document_scanner.dart';
import '../scanner/thumbnail.dart';
import '../settings/app_settings.dart';
import 'review_screen.dart';
import 'staff_picker_screen.dart';

/// The start screen: who is scanning, one large button per document type, and the uploads at a
/// glance (waiting, sending or waiting for the network, problems, sent today).
///
/// A type opens the scanner. Proof of delivery (and every type without a title) has no review:
/// each scan is queued at once, a short buzz confirms it, and the scanner opens again, so the
/// scanner's own Done is the only tap per paper. A general document shows the review screen
/// with the optional title and note and one "Next". Closing the scanner returns here. With only
/// one type the scanner opens at once.
class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    required this.session,
    required this.queue,
    required this.scanner,
    required this.settings,
    required this.onOpenSettings,
    this.thumbnailer = thumbnailOfFile,
    this.confirmQueued = vibrateOnce,
  });

  final DeviceSession session;
  final UploadQueue queue;
  final DocumentScanner scanner;
  final AppSettings settings;
  final VoidCallback onOpenSettings;

  /// Makes the upload thumbnail from the first page image.
  final Future<Uint8List?> Function(File? firstPage) thumbnailer;

  /// The short buzz after each queued scan.
  final Future<void> Function() confirmQueued;

  static const allSentText = 'All scans sent';
  static const retryingText = 'Waiting for network — will retry';
  static const sendingText = 'Sending…';

  /// The short line under a type button, for the types the app knows.
  static String? hintOf(String code) => switch (code) {
    'POD' => 'Scan delivery notes',
    QueuedDocument.generalType => 'Scan any other document',
    _ => null,
  };

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  static const _fallbackTypes = [DocumentType(code: 'POD', displayName: 'Proof of delivery')];

  StreamSubscription<List<QueuedDocument>>? _subscription;
  List<QueuedDocument> _items = const [];
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
    if (mounted) {
      setState(() => _items = items);
    }
  }

  /// A new config (start, coming to the front, enrolment): the type buttons and the name follow.
  void _onConfigChanged() {
    if (mounted) {
      setState(() {});
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
      MaterialPageRoute(
        builder: (context) => StaffPickerScreen(staff: staffList, currentId: remembered),
      ),
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

  /// Scanner, queue, scanner again, until the person closes the scanner (or, for a general
  /// document, goes back from the review screen).
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
      final withReview = type.code == QueuedDocument.generalType;
      // The queue of this screen. On a revoke or reset the session holds it but keeps it open, so
      // a scan that ends after this screen is gone is still saved, with the type and name it was
      // taken under; it uploads after the next enrolment.
      final queue = widget.queue;
      while (_ready) {
        final staffId = _staff?.id;
        final document = await widget.scanner.scan();
        if (document == null) {
          break; // The scanner's X or back: the start screen.
        }
        final thumbnail = widget.thumbnailer(document.firstPage);

        if (!_ready || !mounted) {
          // The phone was removed or reset while the camera was open: keep the scan as it is.
          await _keep(queue, document, type.code, staffId, thumbnail: await thumbnail);
          break;
        }

        if (!withReview) {
          await _keep(queue, document, type.code, staffId, thumbnail: await thumbnail);
          continue;
        }

        if (!context.mounted) {
          break;
        }
        final result = await Navigator.of(context).push<ReviewResult>(
          MaterialPageRoute(
            builder: (context) => ReviewScreen(document: document, typeName: type.displayName, withTitle: true),
          ),
        );
        // Every way out keeps the scan. Null: the app closed the screen (the phone was removed).
        await _keep(
          queue,
          document,
          type.code,
          staffId,
          title: result?.title,
          note: result?.note,
          thumbnail: await thumbnail,
        );
        if (result?.action != ReviewAction.next) {
          break;
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
    try {
      await widget.confirmQueued();
    } on Object {
      // A buzz never stops the next scan.
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

  String get _deviceName {
    final fromConfig = widget.session.config?.deviceName ?? '';
    return fromConfig.isNotEmpty ? fromConfig : widget.session.credentials?.deviceName ?? '';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final showStaff = _staffList.isNotEmpty;
    final problems = [
      for (final item in _items)
        if (item.state == QueuedDocument.failed) item,
    ];
    final waiting = _items
        .where((item) => item.state == QueuedDocument.waiting || item.state == QueuedDocument.uploading)
        .toList();
    final types = _types;

    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 52,
        titleTextStyle: theme.textTheme.titleMedium?.copyWith(
          color: theme.colorScheme.onSurfaceVariant,
          fontWeight: FontWeight.w600,
        ),
        title: const Text('PVM Scanner'),
        actions: [
          IconButton(
            iconSize: 28,
            tooltip: 'Settings',
            onPressed: widget.onOpenSettings,
            icon: const Icon(Icons.settings_outlined),
          ),
        ],
      ),
      // One scroll view: the buttons sit at the bottom when the screen has room, and scroll with
      // the rest on a small phone with large text.
      body: SafeArea(
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: _WhoRow(
                deviceName: _deviceName,
                staffName: showStaff ? (_staff?.name ?? 'Choose name') : null,
                onChange: showStaff && !_scanning ? _changeStaff : null,
              ),
            ),
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              sliver: SliverToBoxAdapter(
                child: _UploadStatus(queue: widget.queue, waiting: waiting.length, problems: problems.length),
              ),
            ),
            if (problems.isNotEmpty)
              SliverList.list(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 24, 16, 8),
                    child: Text('Not sent', style: theme.textTheme.titleMedium),
                  ),
                  const Divider(height: 1),
                  for (final item in problems) ...[
                    _ProblemTile(
                      key: ValueKey(item.clientDocumentId),
                      item: item,
                      thumbnail: widget.queue.thumbnailOf(item),
                      typeName: _typeName(item.documentType),
                      onTryAgain: () => widget.queue.retry(item.clientDocumentId),
                      onRemove: () => _confirmRemove(item),
                    ),
                    const Divider(height: 1),
                  ],
                ],
              ),
            SliverFillRemaining(
              hasScrollBody: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 24, 16, 16),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.end,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (final (index, type) in types.indexed) ...[
                      if (index > 0) const SizedBox(height: 12),
                      _TypeButton(
                        main: index == 0,
                        icon: _iconOf(type.code),
                        label: type.displayName,
                        hint: HomeScreen.hintOf(type.code),
                        onPressed: _scanning ? null : () => _scanFlow(type),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// One document type. The first type is the main action (filled); the others are outlined.
class _TypeButton extends StatelessWidget {
  const _TypeButton({
    required this.main,
    required this.icon,
    required this.label,
    required this.hint,
    required this.onPressed,
  });

  final bool main;
  final IconData icon;
  final String label;
  final String? hint;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(16));
    final size = Size.fromHeight(main ? 96 : 72);
    const padding = EdgeInsets.symmetric(horizontal: 20, vertical: 12);
    final labelStyle = TextStyle(fontSize: main ? 24 : 20, fontWeight: FontWeight.w600);

    final content = Row(
      children: [
        Icon(icon, size: main ? 36 : 28),
        const SizedBox(width: 16),
        Expanded(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(label, style: labelStyle),
              if (hint != null)
                Text(
                  hint!,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: main ? theme.colorScheme.onPrimary : theme.colorScheme.onSurfaceVariant,
                  ),
                ),
            ],
          ),
        ),
      ],
    );

    if (main) {
      return FilledButton(
        style: FilledButton.styleFrom(minimumSize: size, padding: padding, shape: shape),
        onPressed: onPressed,
        child: content,
      );
    }
    return OutlinedButton(
      style: OutlinedButton.styleFrom(
        minimumSize: size,
        padding: padding,
        shape: shape,
        side: BorderSide(color: theme.colorScheme.outline, width: 1.5),
      ),
      onPressed: onPressed,
      child: content,
    );
  }
}

/// One line: all sent, sending, waiting for the network or not sent; then the scans sent today.
class _UploadStatus extends StatelessWidget {
  const _UploadStatus({required this.queue, required this.waiting, required this.problems});

  final UploadQueue queue;
  final int waiting;

  /// Scans the server refused (400 or 413), listed under "Not sent".
  final int problems;

  static String notSentText(int count) =>
      count == 1 ? '1 scan not sent — see below' : '$count scans not sent — see below';

  static String _clock(DateTime time) {
    final local = time.toLocal();
    String two(int value) => value.toString().padLeft(2, '0');
    final clock = '${two(local.hour)}:${two(local.minute)}';
    final now = DateTime.now();
    if (local.year == now.year && local.month == now.month && local.day == now.day) {
      return clock;
    }
    return '${local.day}/${local.month} $clock';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const green = Color(0xFF1B6B2F);
    const amber = Color(0xFF8A5300);
    return ValueListenableBuilder<QueueStatus>(
      valueListenable: queue.status,
      builder: (context, status, _) {
        final (Widget icon, String text, Color color) = switch (status) {
          _ when waiting == 0 && problems == 0 => (
            const Icon(Icons.check_circle, size: 28, color: green),
            HomeScreen.allSentText,
            green,
          ),
          _ when waiting == 0 => (
            Icon(Icons.error, size: 28, color: theme.colorScheme.error),
            notSentText(problems),
            theme.colorScheme.error,
          ),
          QueueStatus.retrying => (const Icon(Icons.wifi_off, size: 28, color: amber), HomeScreen.retryingText, amber),
          QueueStatus.sending => (
            const SizedBox.square(dimension: 24, child: CircularProgressIndicator(strokeWidth: 3)),
            HomeScreen.sendingText,
            theme.colorScheme.primary,
          ),
          QueueStatus.idle => (const Icon(Icons.schedule, size: 28, color: amber), 'Waiting to send', amber),
        };

        final details = theme.textTheme.bodyLarge?.copyWith(color: theme.colorScheme.onSurfaceVariant);
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(width: 32, height: 32, child: Center(child: icon)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    text,
                    style: theme.textTheme.titleLarge?.copyWith(color: color, fontWeight: FontWeight.w600),
                  ),
                  if (waiting > 0) Text('$waiting waiting', style: details),
                  if (waiting > 0 && problems > 0)
                    Text(
                      notSentText(problems),
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: theme.colorScheme.error,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ValueListenableBuilder<SentCount>(
                    valueListenable: queue.sent,
                    builder: (context, sent, _) {
                      final last = sent.lastSentAt;
                      return Wrap(
                        spacing: 8,
                        children: [
                          Text('Sent today: ${sent.today(DateTime.now())}', style: details),
                          if (last != null) Text('· last ${_clock(last)}', style: details),
                        ],
                      );
                    },
                  ),
                ],
              ),
            ),
          ],
        );
      },
    );
  }
}

/// The phone's name and who is scanning, with Change when the server has names.
class _WhoRow extends StatelessWidget {
  const _WhoRow({required this.deviceName, required this.staffName, required this.onChange});

  final String deviceName;
  final String? staffName;
  final VoidCallback? onChange;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = theme.textTheme.titleMedium?.copyWith(color: theme.colorScheme.onSurfaceVariant);
    final parts = [
      if (deviceName.isNotEmpty) Text(deviceName, style: style, overflow: TextOverflow.ellipsis),
      if (staffName != null)
        Text(
          staffName!,
          style: style?.copyWith(color: theme.colorScheme.onSurface, fontWeight: FontWeight.w600),
          overflow: TextOverflow.ellipsis,
        ),
    ];
    if (parts.isEmpty) {
      return const SizedBox.shrink();
    }
    return InkWell(
      onTap: onChange,
      child: Padding(
        padding: EdgeInsets.fromLTRB(16, 4, onChange == null ? 16 : 8, 4),
        child: SizedBox(
          height: 48,
          child: Row(
            children: [
              Icon(Icons.smartphone, size: 24, color: theme.colorScheme.onSurfaceVariant),
              const SizedBox(width: 8),
              Flexible(child: parts.first),
              for (final part in parts.skip(1)) ...[Text('  ·  ', style: style), Flexible(child: part)],
              const Spacer(),
              if (onChange != null) TextButton(onPressed: onChange, child: const Text('Change')),
            ],
          ),
        ),
      ),
    );
  }
}

/// A scan the server refused (400 or 413): the reason, Try again and Remove. Transport problems
/// never show here; the queue retries those by itself.
class _ProblemTile extends StatelessWidget {
  const _ProblemTile({
    super.key,
    required this.item,
    required this.thumbnail,
    required this.typeName,
    required this.onTryAgain,
    required this.onRemove,
  });

  final QueuedDocument item;
  final File? thumbnail;
  final String typeName;
  final VoidCallback onTryAgain;
  final VoidCallback onRemove;

  static String _time(DateTime time) {
    final local = time.toLocal();
    String two(int value) => value.toString().padLeft(2, '0');
    return '${local.day}/${local.month} ${two(local.hour)}:${two(local.minute)}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pages = item.pageCount == 1 ? '1 page' : '${item.pageCount} pages';
    final title = item.title ?? (item.documentType == QueuedDocument.generalType ? 'Untitled' : typeName);
    final details = [if (title != typeName) typeName, pages, _time(item.createdAt)].join(' · ');

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
                    Icon(Icons.error, size: 28, color: theme.colorScheme.error),
                    const SizedBox(width: 6),
                    Flexible(
                      child: Text(
                        'Problem',
                        style: theme.textTheme.titleMedium?.copyWith(
                          color: theme.colorScheme.error,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
                if (item.lastError != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(item.lastError!, style: theme.textTheme.bodyMedium),
                  ),
                Wrap(
                  spacing: 8,
                  children: [
                    TextButton.icon(
                      onPressed: onTryAgain,
                      icon: const Icon(Icons.refresh),
                      label: const Text('Try again'),
                    ),
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
