package za.co.pvm.scanner

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // One short buzz per queued scan. The vibrator works even when the system's touch
        // feedback is off, which HapticFeedback depends on.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "pvm.scanner/vibrate").setMethodCallHandler { call, result ->
            if (call.method != "once") {
                result.notImplemented()
                return@setMethodCallHandler
            }
            val millis = (call.argument<Int>("millis") ?: 80).toLong()
            val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                (getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
            } else {
                @Suppress("DEPRECATION")
                getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
            }
            if (vibrator.hasVibrator()) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    vibrator.vibrate(VibrationEffect.createOneShot(millis, VibrationEffect.DEFAULT_AMPLITUDE))
                } else {
                    @Suppress("DEPRECATION")
                    vibrator.vibrate(millis)
                }
            }
            result.success(null)
        }
    }
}
