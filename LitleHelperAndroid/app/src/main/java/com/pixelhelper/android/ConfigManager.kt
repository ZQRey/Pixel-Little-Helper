package com.pixelhelper.android

import android.content.Context
import android.os.Build
import android.provider.Settings
import com.google.gson.Gson
import com.google.gson.annotations.SerializedName
import java.io.File

data class AppConfig(
    @SerializedName("serverUrl") var serverUrl: String = "http://10.0.0.1:5000",
    @SerializedName("wireguardEndpoint") var wireguardEndpoint: String = "vpn.hospital.local:51820",
    @SerializedName("wireguardServerPublicKey") var wireguardServerPublicKey: String = "",
    @SerializedName("deviceId") var deviceId: String = "",
    @SerializedName("serverDeviceId") var serverDeviceId: Int = 0,
    @SerializedName("wireguardClientPrivateKey") var wireguardClientPrivateKey: String = "",
    @SerializedName("wireguardClientPublicKey") var wireguardClientPublicKey: String = "",
    @SerializedName("wireguardInternalIp") var wireguardInternalIp: String = "10.88.0.50"
)

object ConfigManager {
    private const val CONFIG_FILE_NAME = "config.json"
    private val gson = Gson()
    private var cachedConfig: AppConfig? = null

    fun getConfig(context: Context): AppConfig {
        cachedConfig?.let { return it }

        val file = getConfigFile(context)
        val config = if (file.exists()) {
            try {
                gson.fromJson(file.readText(), AppConfig::class.java) ?: AppConfig()
            } catch (e: Exception) {
                AppConfig()
            }
        } else {
            AppConfig()
        }

        if (config.deviceId.isBlank()) {
            config.deviceId = getHardwareDeviceId(context)
        }

        cachedConfig = config
        saveConfig(context, config)
        return config
    }

    fun saveConfig(context: Context, config: AppConfig) {
        cachedConfig = config
        try {
            val file = getConfigFile(context)
            file.parentFile?.mkdirs()
            file.writeText(gson.toJson(config))
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun getConfigFile(context: Context): File {
        val externalDir = context.getExternalFilesDir(null)
        val externalFile = if (externalDir != null) File(externalDir, CONFIG_FILE_NAME) else null
        if (externalFile != null && externalFile.exists()) {
            return externalFile
        }
        return File(context.filesDir, CONFIG_FILE_NAME)
    }

    fun getHardwareDeviceId(context: Context): String {
        return try {
            val androidId = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
            if (!androidId.isNullOrBlank()) {
                "android-${androidId}"
            } else {
                "android-${Build.SERIAL}-${Build.MODEL}".replace(" ", "_")
            }
        } catch (e: Exception) {
            "android-${Build.MODEL}-${System.currentTimeMillis()}".replace(" ", "_")
        }
    }
}

