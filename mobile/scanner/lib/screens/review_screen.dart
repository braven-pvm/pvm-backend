import 'package:flutter/material.dart';

import '../scanner/document_scanner.dart';

/// What the person chose on the review screen.
enum ReviewAction {
  /// Send this scan and open the scanner for the next paper.
  send,

  /// Send this scan and go back to the start screen.
  finish,

  /// Throw this scan away and open the scanner again.
  scanAgain,

  /// Throw this scan away and go back to the start screen (the back key, then "Delete it").
  discard,
}

class ReviewResult {
  const ReviewResult(this.action, {this.title, this.note});

  final ReviewAction action;
  final String? title;
  final String? note;
}

/// After the scanner: page 1, the page count, the optional fields, and Send. Nothing has to be
/// typed. Closes with a [ReviewResult].
class ReviewScreen extends StatefulWidget {
  const ReviewScreen({super.key, required this.document, required this.typeName, required this.withTitle});

  final ScannedDocument document;
  final String typeName;

  /// General documents have a title; proof of delivery only a note.
  final bool withTitle;

  static const titleLabel = 'Title (optional)';
  static const noteLabel = 'Note (optional)';

  @override
  State<ReviewScreen> createState() => _ReviewScreenState();
}

class _ReviewScreenState extends State<ReviewScreen> {
  final _title = TextEditingController();
  final _note = TextEditingController();

  @override
  void dispose() {
    _title.dispose();
    _note.dispose();
    super.dispose();
  }

  void _close(ReviewAction action) {
    FocusScope.of(context).unfocus();
    Navigator.of(context).pop(ReviewResult(
      action,
      title: widget.withTitle ? _title.text : null,
      note: _note.text,
    ));
  }

  /// The back key: a scan is never thrown away without asking.
  Future<void> _onBack() async {
    final action = await showDialog<ReviewAction>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Send this scan?'),
        content: const Text('If you delete it, it is not sent.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(ReviewAction.discard),
            child: const Text('Delete it'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(minimumSize: const Size(96, 48)),
            onPressed: () => Navigator.of(context).pop(ReviewAction.finish),
            child: const Text('Send'),
          ),
        ],
      ),
    );
    if (!mounted || action == null) {
      return;
    }
    _close(action);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pages = widget.document.pageCount == 1 ? '1 page' : '${widget.document.pageCount} pages';
    final imageHeight = MediaQuery.sizeOf(context).height * 0.38;

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) {
          _onBack();
        }
      },
      child: Scaffold(
        appBar: AppBar(
          automaticallyImplyLeading: false,
          title: Text(widget.typeName),
        ),
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
            children: [
              _FirstPage(document: widget.document, height: imageHeight),
              const SizedBox(height: 8),
              Text(pages, textAlign: TextAlign.center, style: theme.textTheme.titleMedium),
              const SizedBox(height: 16),
              if (widget.withTitle) ...[
                TextField(
                  controller: _title,
                  maxLength: 120,
                  textCapitalization: TextCapitalization.sentences,
                  textInputAction: TextInputAction.next,
                  style: theme.textTheme.titleMedium,
                  decoration: const InputDecoration(
                    labelText: ReviewScreen.titleLabel,
                    border: OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
              ],
              TextField(
                controller: _note,
                maxLength: 1000,
                minLines: 1,
                maxLines: 4,
                textCapitalization: TextCapitalization.sentences,
                textInputAction: TextInputAction.done,
                style: theme.textTheme.titleMedium,
                decoration: const InputDecoration(
                  labelText: ReviewScreen.noteLabel,
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(80)),
                onPressed: () => _close(ReviewAction.send),
                icon: const Icon(Icons.send, size: 32),
                label: const Text('Send', style: TextStyle(fontSize: 24)),
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: () => _close(ReviewAction.finish),
                icon: const Icon(Icons.done_all, size: 28),
                label: const Text('Send and finish'),
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: () => _close(ReviewAction.scanAgain),
                icon: const Icon(Icons.refresh, size: 28),
                label: const Text('Scan again'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FirstPage extends StatelessWidget {
  const _FirstPage({required this.document, required this.height});

  final ScannedDocument document;
  final double height;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final placeholder = Center(
      child: Icon(Icons.description_outlined, size: 72, color: theme.colorScheme.outline),
    );
    final page = document.firstPage;
    return Container(
      height: height,
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
      ),
      padding: const EdgeInsets.all(8),
      child: page == null
          ? placeholder
          : Image.file(
              page,
              fit: BoxFit.contain,
              cacheHeight: 1200,
              errorBuilder: (_, _, _) => placeholder,
            ),
    );
  }
}
