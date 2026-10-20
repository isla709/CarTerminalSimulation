package com.cts.terminal_simulation

import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import android.app.Activity
import android.content.Intent
import android.os.Handler
import android.os.Looper
import java.util.concurrent.Executors
import java.io.ByteArrayOutputStream

class MainActivity : FlutterActivity() {
    private var saveResult: MethodChannel.Result? = null
    private var saveBytes: ByteArray? = null
    private val fileExecutor = Executors.newSingleThreadExecutor()
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "cts/platform")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "dataDirectory" -> result.success(filesDir.absolutePath)
                    "openDocument" -> {
                        if (saveResult != null) {
                            result.error("busy", "文件选择器正在使用", null)
                        } else {
                            saveResult = result
                            saveBytes = null
                            try {
                                startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                                    addCategory(Intent.CATEGORY_OPENABLE)
                                    type = "*/*"
                                }, 10782)
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
    }
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != 10781 && requestCode != 10782) return
        val callback = saveResult ?: return
        val bytes = saveBytes
        saveResult = null
        saveBytes = null
        val uri = data?.data
        if (resultCode != Activity.RESULT_OK || uri == null) {
            callback.success(if (requestCode == 10782) null else false)
            return
        }
        fileExecutor.execute {
            try {
                if (requestCode == 10782) {
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
                Handler(Looper.getMainLooper()).post { callback.error("document-failed", "文件读写失败或路线超过 25 MiB", null) }
            }
        }
    }
    override fun onDestroy() {
        saveResult?.error("activity-destroyed", "文件操作已取消", null)
        saveResult = null
        saveBytes = null
        fileExecutor.shutdown()
        super.onDestroy()
    }
}
