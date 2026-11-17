package za.co.pvm.scanner

import android.content.pm.PackageManager
import android.content.pm.Signature
import android.os.Build
import android.util.Base64
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.net.URLEncoder
import java.security.MessageDigest

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "za.co.pvm.scanner/app")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "redirectUri" -> result.success(redirectUri())
                    else -> result.notImplemented()
                }
            }
    }

    /**
     * The MSAL redirect URI of this APK: msauth://<package>/<URL-encoded Base64 SHA-1 of the
     * signing certificate>. This is the value an Entra administrator registers for the Android
     * platform of the app registration.
     */
    private fun redirectUri(): String {
        val digest = MessageDigest.getInstance("SHA-1").digest(signingCertificate().toByteArray())
        val hash = Base64.encodeToString(digest, Base64.NO_WRAP)
        return "msauth://$packageName/${URLEncoder.encode(hash, "UTF-8")}"
    }

    private fun signingCertificate(): Signature {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            val info = packageManager.getPackageInfo(packageName, PackageManager.GET_SIGNING_CERTIFICATES)
            val signingInfo = info.signingInfo ?: error("The app has no signing information.")
            val signatures = if (signingInfo.hasMultipleSigners()) {
                signingInfo.apkContentsSigners
            } else {
                signingInfo.signingCertificateHistory
            }
            return signatures.first()
        }

        @Suppress("DEPRECATION")
        val info = packageManager.getPackageInfo(packageName, PackageManager.GET_SIGNATURES)
        @Suppress("DEPRECATION")
        return info.signatures!!.first()
    }
}
