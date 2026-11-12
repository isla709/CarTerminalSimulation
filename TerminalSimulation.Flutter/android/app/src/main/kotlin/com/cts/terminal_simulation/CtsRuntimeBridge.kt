package com.cts.terminal_simulation

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.location.GnssStatus
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings
import io.flutter.plugin.common.BinaryMessenger
import io.flutter.plugin.common.MethodChannel
import java.lang.ref.WeakReference

class CtsRuntimeBridge(private val app: CtsApplication, messenger: BinaryMessenger) {
    private val channel = MethodChannel(messenger, "cts/runtime")
    private val location = app.getSystemService(Context.LOCATION_SERVICE) as LocationManager
    private var activity = WeakReference<Activity>(null)
    private var pending: MethodChannel.Result? = null
    private var wantedConnection = false
    private var wantedLocation = false
    private var notificationAsked = false
    private var locating = false
    var connection = false
        private set
    var positioning = false
        private set
    private val listener = object : LocationListener {
        override fun onLocationChanged(fix: Location) {
            if (!positioning) return
            val source = when (fix.provider) {
                LocationManager.GPS_PROVIDER -> "gnss"
                LocationManager.NETWORK_PROVIDER -> "network"
                LocationManager.FUSED_PROVIDER -> "fused"
                else -> "unknown"
            }
            emit(mapOf("type" to "fix", "latitude" to fix.latitude, "longitude" to fix.longitude,
                "altitude" to (if (fix.hasAltitude()) fix.altitude else null),
                "speed" to (if (fix.hasSpeed()) fix.speed.toDouble() else null),
                "bearing" to (if (fix.hasBearing()) fix.bearing.toDouble() else null),
                "accuracy" to fix.accuracy.toDouble(), "time" to fix.time,
                "ageMs" to ((SystemClock.elapsedRealtimeNanos() - fix.elapsedRealtimeNanos) / 1000000L),
                "source" to source, "mock" to fix.isFromMockProvider))
        }
        override fun onProviderDisabled(provider: String) { providersChanged() }
        override fun onProviderEnabled(provider: String) { providersChanged() }
        @Deprecated("Legacy callback") override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
    }
    private val satelliteCallback = object : GnssStatus.Callback() {
        override fun onSatelliteStatusChanged(value: GnssStatus) {
            if (!positioning) return
            emit(mapOf("type" to "satellites", "visible" to value.satelliteCount,
                "used" to (0 until value.satelliteCount).count { value.usedInFix(it) }))
        }
        override fun onStopped() { if (positioning) emit(mapOf("type" to "satellites", "visible" to 0, "used" to 0)) }
    }
    init {
        channel.setMethodCallHandler { call, result ->
            if (call.method == "backgroundSettings") {
                try {
                    app.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    result.success(null)
                } catch (_: Exception) { result.error("settings-unavailable", "请在系统设置中允许 CTS 后台运行", null) }
                return@setMethodCallHandler
            }
            if (call.method != "configure") { result.notImplemented(); return@setMethodCallHandler }
            if (pending != null) { result.error("busy", "系统授权处理中", null); return@setMethodCallHandler }
            wantedConnection = call.argument<Boolean>("connection") == true
            wantedLocation = call.argument<Boolean>("location") == true
            pending = result
            try {
                val permissions = mutableListOf<String>()
                if (wantedLocation && !hasLocationPermission()) {
                    permissions.add(Manifest.permission.ACCESS_FINE_LOCATION)
                    permissions.add(Manifest.permission.ACCESS_COARSE_LOCATION)
                }
                if ((wantedConnection || wantedLocation) && Build.VERSION.SDK_INT >= 33 && !notificationAsked &&
                    app.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                    notificationAsked = true
                    permissions.add(Manifest.permission.POST_NOTIFICATIONS)
                }
                if (permissions.isNotEmpty()) {
                    val host = activity.get() ?: throw IllegalStateException("请回到应用前台启用后台运行或定位")
                    host.requestPermissions(permissions.toTypedArray(), 10783)
                } else apply()
            } catch (error: Exception) { fail(error) }
        }
    }
    fun attach(host: Activity) { activity = WeakReference(host) }
    fun detach(host: Activity) {
        if (activity.get() == host) activity.clear()
        pending?.error("activity-destroyed", "授权已取消，请重新操作", null)
        pending = null
    }
    fun permissionsReturned() { try { apply() } catch (error: Exception) { fail(error) } }
    private fun fail(error: Exception) {
        wantedConnection = connection
        wantedLocation = positioning
        pending?.error("runtime-unavailable", error.message ?: "后台任务启动失败", null)
        pending = null
    }
    private fun apply() {
        if (wantedLocation && !hasLocationPermission())
            throw IllegalStateException("请授予位置权限后使用设备定位")
        if (wantedLocation && enabledProviders().isEmpty())
            throw IllegalStateException("没有已开启的系统定位服务，请检查定位开关")
        val oldConnection = connection
        val oldLocation = positioning
        connection = wantedConnection
        positioning = wantedLocation
        try {
            if (connection || positioning) {
                val intent = Intent(app, CtsConnectionService::class.java)
                if (Build.VERSION.SDK_INT >= 26) app.startForegroundService(intent) else app.startService(intent)
            } else app.stopService(Intent(app, CtsConnectionService::class.java))
            if (positioning && !locating) {
                var requested = 0
                for (provider in availableProviders()) {
                    try {
                        location.requestLocationUpdates(provider, 1000L, 0f, listener, Looper.getMainLooper())
                        requested++
                    } catch (_: Exception) { /* A provider may be unavailable with approximate permission. */ }
                }
                if (requested == 0) throw IllegalStateException("系统定位服务不可用，请检查权限和定位设置")
                locating = true
                if (Build.VERSION.SDK_INT >= 24) {
                    try { location.registerGnssStatusCallback(satelliteCallback, Handler(Looper.getMainLooper())) } catch (_: Exception) {}
                }
                providersChanged()
            } else if (!positioning && locating) stopLocation()
            pending?.success(null)
            pending = null
        } catch (error: Exception) {
            connection = oldConnection
            positioning = oldLocation
            if (!positioning) stopLocation()
            if (!connection && !positioning) app.stopService(Intent(app, CtsConnectionService::class.java))
            throw error
        }
    }
    private fun stopLocation() {
        try { location.removeUpdates(listener) } catch (_: Exception) {}
        if (Build.VERSION.SDK_INT >= 24) {
            try { location.unregisterGnssStatusCallback(satelliteCallback) } catch (_: Exception) {}
        }
        locating = false
    }
    fun stopFromSystem(message: String) {
        connection = false; positioning = false
        wantedConnection = false; wantedLocation = false
        stopLocation()
        emit(mapOf("type" to "stop", "message" to message))
    }
    private fun hasLocationPermission() =
        app.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
        app.checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
    private fun availableProviders(): List<String> {
        val all = location.allProviders
        val fine = app.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
        return listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER, LocationManager.FUSED_PROVIDER)
            .filter { all.contains(it) && (it != LocationManager.GPS_PROVIDER || fine) }
    }
    private fun enabledProviders() = availableProviders().filter { location.isProviderEnabled(it) }
    private fun providersChanged() {
        if (!positioning) return
        if (!hasLocationPermission()) {
            status("位置权限不可用，请在系统设置中检查授权", true)
            return
        }
        val enabled = enabledProviders()
        if (enabled.isEmpty()) {
            status("系统定位已关闭或无可用定位服务", true)
        } else {
            val names = enabled.map { when (it) {
                LocationManager.GPS_PROVIDER -> "GNSS"
                LocationManager.NETWORK_PROVIDER -> "网络定位"
                LocationManager.FUSED_PROVIDER -> "融合定位"
                else -> "系统定位"
            } }.joinToString("、")
            status("可用服务：$names；等待系统更新位置")
        }
    }
    fun status(text: String, invalidate: Boolean = false) = emit(mapOf("type" to "status", "message" to text, "invalidate" to invalidate))
    fun emit(value: Map<String, Any?>) { channel.invokeMethod("event", value) }
}
