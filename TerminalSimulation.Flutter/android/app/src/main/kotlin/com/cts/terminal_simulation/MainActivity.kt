package com.cts.terminal_simulation

import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.os.Handler
import android.os.Looper
import java.util.concurrent.Executors
import java.io.ByteArrayOutputStream
import java.io.File
import io.flutter.embedding.engine.dart.DartExecutor

class MainActivity : FlutterActivity() {
    override fun provideFlutterEngine(context: Context): FlutterEngine = (application as CtsApplication).engine()
    override fun shouldDestroyEngineWithHost(): Boolean = false
    private var saveResult: MethodChannel.Result? = null
    private var saveBytes: ByteArray? = null
    private var documentChannel: MethodChannel? = null
    private val fileExecutor = Executors.newSingleThreadExecutor()
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        (application as CtsApplication).runtime.attach(this)
        documentChannel = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "cts/platform")
        documentChannel?.setMethodCallHandler { call, result ->
                when (call.method) {
                    "dataDirectory" -> result.success(filesDir.absolutePath)
                    "openDocument", "openVideo" -> {
                        if (saveResult != null) {
                            result.error("busy", "文件选择器正在使用", null)
                        } else {
                            saveResult = result
                            saveBytes = null
                            try {
                                startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                                    addCategory(Intent.CATEGORY_OPENABLE)
                                    type = "*/*"
                                    if (call.method == "openVideo") {
                                        putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("video/*", "application/octet-stream"))
                                    }
                                }, if (call.method == "openVideo") 10784 else 10782)
                            } catch (_: Exception) {
                                saveResult = null
                                result.error("picker-unavailable", "无法打开系统文件选择器", null)
                            }
                        }
                    }
                    "saveDocument" -> {
                        val bytes = call.argument<ByteArray>("bytes")
                        val name = call.argument<String>("name") ?: "route.ctsroute"
                        if (saveResult != null) {
                            result.error("busy", "文件选择器正在使用", null)
                        } else if (bytes == null || bytes.size > 25 * 1024 * 1024) {
                            result.error("invalid-data", "文件内容无效或超过 25 MiB", null)
                        } else {
                            saveResult = result
                            saveBytes = bytes
                            try {
                                startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
                                    addCategory(Intent.CATEGORY_OPENABLE)
                                    type = "application/octet-stream"
                                    putExtra(Intent.EXTRA_TITLE, name)
                                }, 10781)
                            } catch (_: Exception) {
                                saveResult = null
                                saveBytes = null
                                result.error("picker-unavailable", "无法打开系统文件选择器", null)
                            }
                        }
                    }
                    else -> result.notImplemented()
                }
            }
        if (!flutterEngine.dartExecutor.isExecutingDart) {
            flutterEngine.dartExecutor.executeDartEntrypoint(DartExecutor.DartEntrypoint.createDefault())
        }
    }
    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 10783) (application as CtsApplication).runtime.permissionsReturned()
    }
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != 10781 && requestCode != 10782 && requestCode != 10784) return
        val callback = saveResult ?: return
        val bytes = saveBytes
        saveResult = null
        saveBytes = null
        val uri = data?.data
        if (resultCode != Activity.RESULT_OK || uri == null) {
            callback.success(if (requestCode == 10781) false else null)
            return
        }
        fileExecutor.execute {
            try {
                if (requestCode == 10784) {
                    val directory = File(filesDir, "video-sources").apply { mkdirs() }
                    val target = File.createTempFile("video-", ".h264", directory)
                    try {
                        contentResolver.openInputStream(uri)?.use { input ->
                            target.outputStream().use { output ->
                                val buffer = ByteArray(64 * 1024)
                                var total = 0L
                                while (true) {
                                    val count = input.read(buffer)
                                    if (count < 0) break
                                    total += count
                                    if (total > 256L * 1024 * 1024) throw IllegalStateException("视频超过 256 MiB")
                                    output.write(buffer, 0, count)
                                }
                            }
                        } ?: throw IllegalStateException("无法打开视频")
                    } catch (error: Exception) {
                        target.delete()
                        throw error
                    }
                    Handler(Looper.getMainLooper()).post { callback.success(target.absolutePath) }
                } else if (requestCode == 10782) {
                    val contents = contentResolver.openInputStream(uri)?.use { input ->
                        val output = ByteArrayOutputStream()
                        val buffer = ByteArray(64 * 1024)
                        var total = 0
                        while (true) {
                            val count = input.read(buffer)
                            if (count < 0) break
                            total += count
                            if (total > 25 * 1024 * 1024) throw IllegalStateException("路线超过 25 MiB")
                            output.write(buffer, 0, count)
                        }
                        output.toByteArray()
                    } ?: throw IllegalStateException("无法打开文件")
                    Handler(Looper.getMainLooper()).post { callback.success(contents) }
                } else {
                    if (bytes == null) throw IllegalStateException("文件内容无效")
                    contentResolver.openOutputStream(uri, "wt")?.use { it.write(bytes) }
                        ?: throw IllegalStateException("无法打开文件")
                    Handler(Looper.getMainLooper()).post { callback.success(true) }
                }
            } catch (_: Exception) {
                Handler(Looper.getMainLooper()).post { callback.error("document-failed", if (requestCode == 10784) "无法读取视频，请选择不超过 256 MiB 的 H.264 裸流文件" else "文件读写失败或路线超过 25 MiB", null) }
            }
        }
    }
    override fun onDestroy() {
        (application as CtsApplication).runtime.detach(this)
        documentChannel?.setMethodCallHandler(null)
        documentChannel = null
        saveResult?.error("activity-destroyed", "文件操作已取消", null)
        saveResult = null
        saveBytes = null
        fileExecutor.shutdown()
        super.onDestroy()
    }
}
