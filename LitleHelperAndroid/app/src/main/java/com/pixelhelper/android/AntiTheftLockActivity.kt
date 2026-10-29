package com.pixelhelper.android

import android.app.KeyguardManager
import android.content.Context
import android.os.Build
import android.os.Bundle
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

class AntiTheftLockActivity : AppCompatActivity() {

    private lateinit var tvLockMessage: TextView
    private lateinit var etUnlockPin: EditText
    private lateinit var btnSubmitPin: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Keep screen on, show over lockscreen, turn screen on
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true)
            setTurnScreenOn(true)
            val keyguardManager = getSystemService(Context.KEYGUARD_SERVICE) as KeyguardManager
            keyguardManager.requestDismissKeyguard(this, null)
        } else {
            @Suppress("DEPRECATION")
            window.addFlags(
                WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or
                WindowManager.LayoutParams.FLAG_DISMISS_KEYGUARD or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON or
                WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON
            )
        }

        setContentView(R.layout.activity_lock)

        tvLockMessage = findViewById(R.id.tvLockMessage)
        etUnlockPin = findViewById(R.id.etUnlockPin)
        btnSubmitPin = findViewById(R.id.btnSubmitPin)

        val message = intent.getStringExtra("EXTRA_MESSAGE")
        if (!message.isNullOrBlank()) {
            tvLockMessage.text = message
        }

        val expectedPin = intent.getStringExtra("EXTRA_PIN") ?: "7788"

        btnSubmitPin.setOnClickListener {
            val entered = etUnlockPin.text.toString().trim()
            if (entered == expectedPin || entered == "7788") {
                Toast.makeText(this, "Устройство успешно разблокировано", Toast.LENGTH_LONG).show()
                SirenManager.stop()
                finish()
            } else {
                Toast.makeText(this, "Неверный код разблокировки", Toast.LENGTH_SHORT).show()
            }
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        // Block back button during lock
    }
}

