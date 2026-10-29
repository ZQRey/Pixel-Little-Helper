package com.pixelhelper.android

import android.app.admin.DevicePolicyManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    private lateinit var tvProtectionStatus: TextView
    private lateinit var tvAdminStatus: TextView
    private lateinit var tvServerUrl: TextView
    private lateinit var tvWgStatus: TextView
    private lateinit var tvWgPublicKey: TextView
    private lateinit var tvDeviceInfo: TextView
    private lateinit var tvBatteryInfo: TextView
    private lateinit var tvLocationInfo: TextView
    private lateinit var btnCopyKey: Button
    private lateinit var btnForceSync: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        tvProtectionStatus = findViewById(R.id.tvProtectionStatus)
        tvAdminStatus = findViewById(R.id.tvAdminStatus)
        tvServerUrl = findViewById(R.id.tvServerUrl)
        tvWgStatus = findViewById(R.id.tvWgStatus)
        tvWgPublicKey = findViewById(R.id.tvWgPublicKey)
        tvDeviceInfo = findViewById(R.id.tvDeviceInfo)
        tvBatteryInfo = findViewById(R.id.tvBatteryInfo)
        tvLocationInfo = findViewById(R.id.tvLocationInfo)
        btnCopyKey = findViewById(R.id.btnCopyKey)
        btnForceSync = findViewById(R.id.btnForceSync)

        // Ensure keys and start service
        WireGuardManager.ensureKeys(this)
        AntiTheftForegroundService.start(this)

        updateUi()

        btnCopyKey.setOnClickListener {
            val config = ConfigManager.getConfig(this)
            if (config.wireguardClientPublicKey.isNotBlank()) {
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                val clip = ClipData.newPlainText("WireGuard Public Key", config.wireguardClientPublicKey)
                clipboard.setPrimaryClip(clip)
                Toast.makeText(this, "Публичный ключ скопирован в буфер обмена", Toast.LENGTH_SHORT).show()
            }
        }

        btnForceSync.setOnClickListener {
            AntiTheftForegroundService.start(this)
            Toast.makeText(this, "Синхронизация с сервером запущена", Toast.LENGTH_SHORT).show()
            updateUi()
        }
    }

    override fun onResume() {
        super.onResume()
        updateUi()
    }

    private fun updateUi() {
        val config = ConfigManager.getConfig(this)
        val isAdmin = DeviceAdminReceiver.isAdminActive(this)

        tvAdminStatus.text = if (isAdmin) {
            "Права администратора устройства: АКТИВНЫ"
        } else {
            "Права администратора устройства: НЕ НАЗНАЧЕНЫ"
        }

        tvServerUrl.text = "Сервер: ${config.serverUrl}"
        tvWgPublicKey.text = if (config.wireguardClientPublicKey.isNotBlank()) config.wireguardClientPublicKey else "Генерация..."
        tvDeviceInfo.text = "Модель: ${Build.MANUFACTURER} ${Build.MODEL} (Android ${Build.VERSION.RELEASE})"
    }
}

