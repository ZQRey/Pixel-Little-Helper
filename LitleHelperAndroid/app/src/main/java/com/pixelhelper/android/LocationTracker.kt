package com.pixelhelper.android

import android.annotation.SuppressLint
import android.content.Context
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.Bundle

data class DeviceLocation(
    val latitude: Double,
    val longitude: Double,
    val accuracy: Float,
    val timeUtc: Long
)

class LocationTracker(private val context: Context) {
    private val locationManager = context.getSystemService(Context.LOCATION_SERVICE) as LocationManager
    private var lastKnownLocation: DeviceLocation? = null

    init {
        startListening()
    }

    @SuppressLint("MissingPermission")
    private fun startListening() {
        val listener = object : LocationListener {
            override fun onLocationChanged(location: Location) {
                lastKnownLocation = DeviceLocation(
                    latitude = location.latitude,
                    longitude = location.longitude,
                    accuracy = location.accuracy,
                    timeUtc = location.time
                )
            }
            override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
            override fun onProviderEnabled(provider: String) {}
            override fun onProviderDisabled(provider: String) {}
        }

        try {
            if (locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER)) {
                locationManager.requestLocationUpdates(
                    LocationManager.GPS_PROVIDER,
                    30_000L,
                    5f,
                    listener
                )
            }
            if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
                locationManager.requestLocationUpdates(
                    LocationManager.NETWORK_PROVIDER,
                    30_000L,
                    10f,
                    listener
                )
            }
        } catch (e: SecurityException) {
            e.printStackTrace()
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    @SuppressLint("MissingPermission")
    fun getCurrentLocation(): DeviceLocation? {
        if (lastKnownLocation != null) return lastKnownLocation

        try {
            val gpsLoc = locationManager.getLastKnownLocation(LocationManager.GPS_PROVIDER)
            val netLoc = locationManager.getLastKnownLocation(LocationManager.NETWORK_PROVIDER)
            val best = when {
                gpsLoc != null && netLoc != null -> if (gpsLoc.time >= netLoc.time) gpsLoc else netLoc
                gpsLoc != null -> gpsLoc
                else -> netLoc
            }

            if (best != null) {
                return DeviceLocation(
                    latitude = best.latitude,
                    longitude = best.longitude,
                    accuracy = best.accuracy,
                    timeUtc = best.time
                )
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        return null
    }
}

