import 'dart:io';

/// One scanned paper document: a single PDF with all its pages.
class ScannedDocument {
  const ScannedDocument({required this.pdf, required this.pageCount, this.pageImages = const []});

  final File pdf;
  final int pageCount;

  /// The scanner's JPEG of each page, when it gave them; page 1 shows on the review screen and
  /// becomes the thumbnail.
  final List<File> pageImages;

  File? get firstPage => pageImages.isEmpty ? null : pageImages.first;

  /// Deletes the page images (the PDF is the document; the queue keeps its own copy).
  Future<void> deleteImages() async {
    for (final image in pageImages) {
      try {
        await image.delete();
      } on FileSystemException {
        // In the cache folder; Android clears it later.
      }
    }
  }

  /// "Scan again": deletes the PDF and the page images of this scan.
  Future<void> discard() async {
    try {
      await pdf.delete();
    } on FileSystemException {
      // In the cache folder; Android clears it later.
    }
    await deleteImages();
  }
}

/// A scan that failed for a reason other than the user closing the scanner.
class ScannerException implements Exception {
  const ScannerException(this.message);

  final String message;

  @override
  String toString() => message;
}

/// Captures one document with the camera.
///
/// ML Kit is the only implementation now. If a phone test shows that some paper shapes need a
/// different edge detector, a second implementation goes behind this interface.
abstract interface class DocumentScanner {
  /// Opens the camera and returns the document, or null when the user closed the scanner.
  /// Throws [ScannerException] when the scanner cannot work.
  Future<ScannedDocument?> scan();
}
