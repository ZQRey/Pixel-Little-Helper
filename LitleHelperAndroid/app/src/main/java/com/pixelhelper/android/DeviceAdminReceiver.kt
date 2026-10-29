package com.pixelhelper.android

import android.app.admin.DeviceAdminReceiver
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.Toast

class DeviceAdminReceiver : DeviceAdminReceiver() {

    override fun onEnabled(context: Context, intent: Intent) {
        super.onEnabled(context, intent)
        Toast.makeText(context, "Pixel Little Helper: права администратора активированы", Toast.LENGTH_SHORT).show()
    }

    override fun onDisableRequested(context: Context, intent: Intent): CharSequence {
        return "ВНИМАНИЕ! Отключение прав администратора сделает устройство уязвимым и отправит экстренное оповещение в IT-отдел."
    }

    override fun onDisabled(context: Context, intent: Intent) {
        super.onDisabled(context, intent)
    }

    companion object {
        fun getComponentName(context: Context): ComponentName {
            return ComponentName(context.applicationContext, DeviceAdminReceiver::class.java)
        }

        fun isAdminActive(context: Context): Boolean {
            val dpm = context.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
            return dpm.isAdminActive(getComponentName(context))
        }

        fun lockNow(context: Context) {
            val dpm = context.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
            if (isAdminActive(context)) {
                dpm.lockNow()
            }
        }

        fun resetPin(context: Context, newPin: String): Boolean {
            val dpm = context.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
            if (!isAdminActive(context) || newPin.isBlank()) return false
            return try {
                dpm.resetPassword(newPin, DevicePolicyManager.RESET_PASSWORD_REQUIRE_ENTRY)
            } catch (e: Exception) {
                e.printStackTrace()
                false
            }
        }
    }
}

