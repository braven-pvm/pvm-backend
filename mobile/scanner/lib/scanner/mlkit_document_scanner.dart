import 'dart:io';

import 'package:flutter/services.dart';
import 'package:google_mlkit_document_scanner/google_mlkit_document_scanner.dart' as mlkit;

import 'document_scanner.dart';

/// [DocumentScanner] with Google's ML Kit document scanner: it finds the paper edges, captures
/// automatically, corrects the perspective, lets the user adjust corners and add pages, and
/// returns one PDF. Needs Google Play services on the phone.
class MlKitDocumentScanner implements DocumentScanner {
  static final _options = mlkit.DocumentScannerOptions(
    documentFormats: {mlkit.DocumentFormat.pdf},
    mode: mlkit.ScannerMode.full,
    isGalleryImport: false,
    pageLimit: 20,
  );

  @override
  Future<ScannedDocument?> scan() async {
    final scanner = mlkit.DocumentScanner(options: _options);
    try {
      final result = await scanner.scanDocument();
      final pdf = result.pdf;
      if (pdf == null) {
        throw const ScannerException('The scanner returned no PDF.');
      }
      return ScannedDocument(pdf: File(pdf.uri), pageCount: pdf.pageCount);
    } on PlatformException catch (error) {
      if (error.message == 'Operation cancelled') {
        return null;
      }
      throw ScannerException(
        'The scanner could not start. It needs Google Play services and a camera. (${error.message})',
      );
    } finally {
      await scanner.close();
    }
  }
}
