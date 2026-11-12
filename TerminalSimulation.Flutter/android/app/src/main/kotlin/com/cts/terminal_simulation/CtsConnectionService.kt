package com.cts.terminal_simulation

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager

class CtsConnectionService : Service() {
    private var wakeLock: PowerManager.WakeLock? = null
    private val runtime get() = (application as CtsApplication).let { it.engine(); it.runtime }
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == "cts.STOP") {
            runtime.stopFromSystem("后台任务已停止")
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        if (!runtime.connection && !runtime.positioning) { stopSelf(); return START_NOT_STICKY }
        try {
            val manager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
            if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(
                NotificationChannel("cts-session", "终端连接与定位", NotificationManager.IMPORTANCE_LOW))
            val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            val stop = PendingIntent.getService(this, 1, Intent(this, CtsConnectionService::class.java).setAction("cts.STOP"),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, "cts-session") else Notification.Builder(this)
            val notification = builder.setSmallIcon(R.drawable.ic_cts_status).setContentTitle("CTS Terminal Studio")
                .setContentText(if (runtime.positioning) "设备系统定位运行中" else "终端连接运行中")
                .setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true)
                .addAction(Notification.Action.Builder(null, "停止任务", stop).build()).build()
            if (Build.VERSION.SDK_INT >= 34) {
                val types = (if (runtime.connection) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0) or
                    (if (runtime.positioning) ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION else 0)
                startForeground(10783, notification, types)
            } else if (Build.VERSION.SDK_INT >= 29) {
                startForeground(10783, notification, if (runtime.positioning) ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION else 0)
            } else startForeground(10783, notification)
            if (wakeLock == null) {
                wakeLock = (getSystemService(POWER_SERVICE) as PowerManager).newWakeLock(
                    PowerManager.PARTIAL_WAKE_LOCK, "cts:terminal-session").apply { acquire() }
            }
        } catch (_: Exception) {
            runtime.stopFromSystem("后台运行不可用，请回到应用前台重新启用")
            stopSelf()
        }
        return START_NOT_STICKY
    }
    override fun onDestroy() {
        if (runtime.connection || runtime.positioning) runtime.stopFromSystem("后台任务已停止")
        wakeLock?.let { if (it.isHeld) it.release() }
        wakeLock = null
        super.onDestroy()
    }
    override fun onTimeout(startId: Int, fgsType: Int) {
        runtime.stopFromSystem("系统已停止后台任务")
        stopSelf()
    }
}
