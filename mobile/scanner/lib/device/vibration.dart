import 'package:flutter/services.dart';

const _channel = MethodChannel('pvm.scanner/vibrate');

/// One short buzz: a scan was queued. Uses the phone's vibrator (it works when the system's touch
/// feedback is off); falls back to a haptic click where the channel is missing.
Future<void> vibrateOnce() async {
  try {
    await _channel.invokeMethod<void>('once', {'millis': 80});
  } on MissingPluginException {
    await HapticFeedback.heavyImpact();
  } on PlatformException {
    await HapticFeedback.heavyImpact();
  }
}
