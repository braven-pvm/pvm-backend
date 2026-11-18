import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

import 'package:image/image.dart' as img;

/// The long side of a thumbnail, in pixels.
const thumbnailLongSide = 480;

/// The largest thumbnail the backend keeps (design 6.12): 200 KB.
const maxThumbnailBytes = 200 * 1024;

/// A JPEG of the first page for the scan list and the workbench: scaled down to [longSide] on
/// the long side (a smaller page keeps its size), quality about 70. When that is larger than
/// [maxBytes], the quality goes down step by step. Null when the image cannot be read or no
/// quality fits; the scan then goes without a thumbnail.
Uint8List? makeThumbnail(
  List<int> jpeg, {
  int longSide = thumbnailLongSide,
  int maxBytes = maxThumbnailBytes,
}) {
  final img.Image? page;
  try {
    page = img.decodeImage(Uint8List.fromList(jpeg));
  } on Object {
    return null;
  }
  if (page == null) {
    return null;
  }

  final oriented = img.bakeOrientation(page);
  final img.Image scaled;
  if (oriented.width >= oriented.height && oriented.width > longSide) {
    scaled = img.copyResize(oriented, width: longSide, interpolation: img.Interpolation.average);
  } else if (oriented.height > oriented.width && oriented.height > longSide) {
    scaled = img.copyResize(oriented, height: longSide, interpolation: img.Interpolation.average);
  } else {
    scaled = oriented;
  }

  for (final quality in const [70, 60, 50, 40, 30, 20]) {
    final bytes = img.encodeJpg(scaled, quality: quality);
    if (bytes.length <= maxBytes) {
      return bytes;
    }
  }
  return null;
}

/// [makeThumbnail] of a page image file, off the UI thread. Null on any failure.
Future<Uint8List?> thumbnailOfFile(File? page) async {
  if (page == null) {
    return null;
  }
  try {
    final bytes = await page.readAsBytes();
    return await Isolate.run(() => makeThumbnail(bytes));
  } on Object {
    return null;
  }
}
