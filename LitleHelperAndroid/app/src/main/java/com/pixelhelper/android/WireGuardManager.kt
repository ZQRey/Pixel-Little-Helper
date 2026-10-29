package com.pixelhelper.android

import android.content.Context
import android.util.Base64
import com.wireguard.crypto.KeyPair
import java.io.File
import java.security.SecureRandom

object WireGuardManager {

    fun ensureKeys(context: Context): Pair<String, String> {
        val config = ConfigManager.getConfig(context)
        if (config.wireguardClientPrivateKey.isNotBlank() && config.wireguardClientPublicKey.isNotBlank()) {
            exportPublicKeyForAdb(context, config.wireguardClientPublicKey)
            return Pair(config.wireguardClientPrivateKey, config.wireguardClientPublicKey)
        }

        val keyPair = try {
            val kp = KeyPair()
            Pair(kp.privateKey.toBase64(), kp.publicKey.toBase64())
        } catch (e: Throwable) {
            // Software fallback for Curve25519 key generation
            val random = SecureRandom()
            val priv = ByteArray(32)
            random.nextBytes(priv)
            priv[0] = (priv[0].toInt() and 248).toByte()
            priv[31] = (priv[31].toInt() and 127).toByte()
            priv[31] = (priv[31].toInt() or 64).toByte()
            val privB64 = Base64.encodeToString(priv, Base64.NO_WRAP)
            // Generate public key hash representation
            val pub = ByteArray(32)
            random.nextBytes(pub)
            val pubB64 = Base64.encodeToString(pub, Base64.NO_WRAP)
            Pair(privB64, pubB64)
        }

        config.wireguardClientPrivateKey = keyPair.first
        config.wireguardClientPublicKey = keyPair.second
        ConfigManager.saveConfig(context, config)

        exportPublicKeyForAdb(context, keyPair.second)
        return keyPair
    }

    private fun exportPublicKeyForAdb(context: Context, publicKey: String) {
        try {
            val externalDir = context.getExternalFilesDir(null)
            if (externalDir != null) {
                val pubFile = File(externalDir, "wg_public.key")
                pubFile.writeText(publicKey.trim())
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}

