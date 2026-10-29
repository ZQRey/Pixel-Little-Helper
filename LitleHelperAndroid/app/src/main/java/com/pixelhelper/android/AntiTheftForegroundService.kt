package com.pixelhelper.android

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.wifi.WifiManager
import android.os.BatteryManager
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.google.gson.Gson
import com.google.gson.annotations.SerializedName
import kotlinx.coroutines.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.concurrent.TimeUnit

class AntiTheftForegroundService : Service() {

    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .build()
    private val gson = Gson()
    private lateinit var locationTracker: LocationTracker

    override fun onCreate() {
        super.onCreate()
        locationTracker = LocationTracker(this)
        startForeground(NOTIFICATION_ID, createNotification())
        WireGuardManager.ensureKeys(this)
        startTelemetryLoop()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        return START_STICKY
    }

    override fun onDestroy() {
        serviceScope.cancel()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun startTelemetryLoop() {
        serviceScope.launch {
            while (isActive) {
                try {
                    ensureRegistered()
                    sendTelemetry()
                } catch (e: Exception) {
                    e.printStackTrace()
                }
                delay(15_000L) // Telemetry interval
            }
        }
    }

    private suspend fun ensureRegistered() {
        val config = ConfigManager.getConfig(this)
        if (config.serverDeviceId > 0) return

        val keys = WireGuardManager.ensureKeys(this)
        val regBody = gson.toJson(
            mapOf(
                "deviceId" to config.deviceId,
                "brandModel" to "${Build.MANUFACTURER} ${Build.MODEL}",
                "serialNumber" to Build.SERIAL,
                "androidVersion" to Build.VERSION.RELEASE,
                "clientVersion" to "1.0.0",
                "wireguardPublicKey" to keys.second
            )
        )

        val request = Request.Builder()
            .url("${config.serverUrl}/api/mobile-devices/register")
            .post(regBody.toRequestBody("application/json".toMediaType()))
            .build()

        try {
            httpClient.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val respJson = response.body?.string() ?: ""
                    val registered = gson.fromJson(respJson, ServerDeviceResponse::class.java)
                    if (registered != null && registered.id > 0) {
                        config.serverDeviceId = registered.id
                        if (!registered.wireguardInternalIp.isNullOrBlank()) {
                            config.wireguardInternalIp = registered.wireguardInternalIp
                        }
                        ConfigManager.saveConfig(this, config)
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private suspend fun sendTelemetry() {
        val config = ConfigManager.getConfig(this)
        if (config.serverDeviceId <= 0) return

        val (batteryPercent, isCharging) = getBatteryStatus()
        val (wifiSsid, wifiBssid) = getWifiStatus()
        val loc = locationTracker.getCurrentLocation()

        val teleBody = gson.toJson(
            mapOf(
                "batteryPercent" to batteryPercent,
                "isCharging" to isCharging,
                "currentWifiSsid" to wifiSsid,
                "currentWifiBssid" to wifiBssid,
                "latitude" to loc?.latitude,
                "longitude" to loc?.longitude,
                "locationAccuracy" to loc?.accuracy
            )
        )

        val request = Request.Builder()
            .url("${config.serverUrl}/api/mobile-devices/${config.serverDeviceId}/telemetry")
            .post(teleBody.toRequestBody("application/json".toMediaType()))
            .build()

        try {
            httpClient.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    val state = gson.fromJson(bodyStr, ServerLockdownState::class.java)
                    if (state != null) {
                        applyLockdownState(state)
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun applyLockdownState(state: ServerLockdownState) {
        if (state.isLockedDown) {
            // 1. PIN Lock
            if (state.lockLevelPin && !state.temporaryLockPin.isNullOrBlank()) {
                DeviceAdminReceiver.resetPin(this, state.temporaryLockPin)
                DeviceAdminReceiver.lockNow(this)
            }

            // 2. Fullscreen Screen Lock
            if (state.lockLevelScreen) {
                val intent = Intent(this, AntiTheftLockActivity::class.java).apply {
                    flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
                    putExtra("EXTRA_MESSAGE", state.lockMessage)
                    putExtra("EXTRA_PIN", state.temporaryLockPin)
                }
                startActivity(intent)
            }

            // 3. Siren
            if (state.isSirenActive) {
                SirenManager.start(this)
            } else {
                SirenManager.stop()
            }
        } else {
            // Recover from lockdown
            SirenManager.stop()
        }
    }

    private fun getBatteryStatus(): Pair<Int, Boolean> {
        return try {
            val filter = IntentFilter(Intent.ACTION_BATTERY_CHANGED)
            val batteryStatus = registerReceiver(null, filter)
            val level = batteryStatus?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
            val scale = batteryStatus?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
            val pct = if (level >= 0 && scale > 0) (level * 100 / scale) else 100
            val status = batteryStatus?.getIntExtra(BatteryManager.EXTRA_STATUS, -1) ?: -1
            val isCharging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
            Pair(pct, isCharging)
        } catch (e: Exception) {
            Pair(100, false)
        }
    }

    private fun getWifiStatus(): Pair<String, String> {
        return try {
            val wm = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            val info = wm.connectionInfo
            val ssid = info.ssid?.replace("\"", "") ?: ""
            val bssid = info.bssid ?: ""
            Pair(ssid, bssid)
        } catch (e: Exception) {
            Pair("", "")
        }
    }

    private fun createNotification(): Notification {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                getString(R.string.service_notification_channel),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = getString(R.string.service_notification_text)
                setShowBadge(false)
            }
            val nm = getSystemService(NotificationManager::class.java)
            nm.createNotificationChannel(channel)
        }

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.service_notification_title))
            .setContentText(getString(R.string.service_notification_text))
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    companion object {
        private const val NOTIFICATION_ID = 9110
        private const val CHANNEL_ID = "pixel_helper_service_channel"

        fun start(context: Context) {
            val intent = Intent(context, AntiTheftForegroundService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }
    }

    private data class ServerDeviceResponse(
        @SerializedName("id") val id: Int,
        @SerializedName("wireguardInternalIp") val wireguardInternalIp: String?
    )

    private data class ServerLockdownState(
        @SerializedName("isLockedDown") val isLockedDown: Boolean,
        @SerializedName("lockLevelScreen") val lockLevelScreen: Boolean,
        @SerializedName("lockLevelPin") val lockLevelPin: Boolean,
        @SerializedName("lockMessage") val lockMessage: String?,
        @SerializedName("temporaryLockPin") val temporaryLockPin: String?,
        @SerializedName("isSirenActive") val isSirenActive: Boolean
    )
}

