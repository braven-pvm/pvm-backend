import 'dart:io';

/// One scanned paper document: a single PDF with all its pages.
class ScannedDocument {
  const ScannedDocument({required this.pdf, required this.pageCount});

  final File pdf;
  final int pageCount;
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
