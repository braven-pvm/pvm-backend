import 'package:flutter/material.dart';

import '../scanner/document_scanner.dart';

/// What the person chose on the review screen. Either way the scan is kept and sent.
enum ReviewAction {
  /// "Next": keep this scan and open the scanner for the next paper.
  next,

  /// Back (the arrow or the phone's back key): keep this scan and go back to the start screen.
  /// The scan was taken, so nobody loses it by pressing back; a bad scan is deleted in the
  /// workbench.
  stop,
}

class ReviewResult {
  const ReviewResult(this.action, {this.title, this.note});

  final ReviewAction action;
  final String? title;
  final String? note;
}

/// After the scanner, for general documents: page 1, the page count, the optional title and note,
/// and one large "Next". Nothing has to be typed. Closes with a [ReviewResult].
class ReviewScreen extends StatefulWidget {
  const ReviewScreen({super.key, required this.document, required this.typeName, required this.withTitle});

  final ScannedDocument document;
  final String typeName;

  /// General documents have a title.
  final bool withTitle;

  static const titleLabel = 'Title (optional)';
  static const noteLabel = 'Note (optional)';
  static const nextLabel = 'Next';

  @override
  State<ReviewScreen> createState() => _ReviewScreenState();
}

class _ReviewScreenState extends State<ReviewScreen> {
  final _title = TextEditingController();
  final _note = TextEditingController();
  var _closed = false;

  @override
  void dispose() {
    _title.dispose();
    _note.dispose();
    super.dispose();
  }

  void _close(ReviewAction action) {
    if (_closed) {
      return;
    }
    _closed = true;
    FocusScope.of(context).unfocus();
    Navigator.of(context).pop(ReviewResult(
      action,
      title: widget.withTitle ? _title.text : null,
      note: _note.text,
    ));
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
          _close(ReviewAction.stop);
        }
      },
      child: Scaffold(
        appBar: AppBar(
          leading: BackButton(onPressed: () => _close(ReviewAction.stop)),
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
                onPressed: () => _close(ReviewAction.next),
                icon: const Icon(Icons.arrow_forward, size: 32),
                label: const Text(ReviewScreen.nextLabel, style: TextStyle(fontSize: 24)),
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
