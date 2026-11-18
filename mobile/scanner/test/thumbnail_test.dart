import 'dart:math' as math;

import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:scanner/scanner/thumbnail.dart';

/// A page-like JPEG with noise, so it does not compress to almost nothing.
List<int> _photo(int width, int height, {int seed = 1}) {
  final random = math.Random(seed);
  final image = img.Image(width: width, height: height);
  for (final pixel in image) {
    final value = random.nextInt(256);
    pixel
      ..r = value
      ..g = (value + 40) % 256
      ..b = random.nextInt(256);
  }
  return img.encodeJpg(image, quality: 95);
}

void main() {
  test('a_tall_page_is_scaled_to_480_on_the_long_side_keeping_its_shape', () {
    final thumbnail = makeThumbnail(_photo(1240, 1754))!;
    final decoded = img.decodeJpg(thumbnail)!;

    expect(decoded.height, 480);
    expect(decoded.width, closeTo(1240 * 480 / 1754, 1));
    expect(thumbnail.length, lessThanOrEqualTo(maxThumbnailBytes));
    expect(thumbnail.take(3), [0xFF, 0xD8, 0xFF]);
  });

  test('a_wide_page_is_scaled_on_its_width', () {
    final decoded = img.decodeJpg(makeThumbnail(_photo(2000, 600))!)!;

    expect(decoded.width, 480);
    expect(decoded.height, closeTo(600 * 480 / 2000, 1));
  });

  test('a_small_page_is_not_made_larger', () {
    final decoded = img.decodeJpg(makeThumbnail(_photo(300, 200))!)!;

    expect(decoded.width, 300);
    expect(decoded.height, 200);
  });

  test('the_quality_goes_down_until_the_jpeg_fits_the_byte_limit', () {
    const limit = 20 * 1024;
    final thumbnail = makeThumbnail(_photo(1240, 1754), maxBytes: limit)!;

    expect(thumbnail.length, lessThanOrEqualTo(limit));
    expect(img.decodeJpg(thumbnail)!.height, 480);
  });

  test('no_thumbnail_when_it_cannot_fit_or_the_image_is_unreadable', () {
    expect(makeThumbnail(_photo(1240, 1754), maxBytes: 100), isNull);
    expect(makeThumbnail([1, 2, 3, 4]), isNull);
  });

  test('the_limits_are_480_px_and_200_kb', () {
    expect(thumbnailLongSide, 480);
    expect(maxThumbnailBytes, 200 * 1024);
  });
}
