package com.sda.mobile.network

import com.sda.mobile.model.QueryTimeEnvelope
import kotlinx.serialization.json.Json
import okhttp3.OkHttpClient

/**
 * Port of SteamAuth.TimeAligner. Codes are generated from device clock time plus a
 * once-per-process offset learned from Steam's QueryTime endpoint, so a slightly-wrong device
 * clock doesn't produce invalid codes.
 */
object TimeAligner {
    @Volatile private var aligned = false
    @Volatile private var timeDifferenceSeconds = 0L

    private val json = Json { ignoreUnknownKeys = true }
    private val client: OkHttpClient by lazy { SteamHttpClient.newClient() }

    fun getSteamTimeCached(): Long =
        (System.currentTimeMillis() / 1000L) + timeDifferenceSeconds

    /** Call once at app/session start (and it's safe to call repeatedly - only aligns once). */
    suspend fun alignIfNeeded() {
        if (aligned) return
        try {
            val currentTime = System.currentTimeMillis() / 1000L
            val response = SteamHttpClient.post(client, ApiEndpoints.QUERY_TIME, mapOf("steamid" to "0"))
            val parsed = json.decodeFromString<QueryTimeEnvelope>(response)
            val serverTime = parsed.response?.serverTime ?: return
            timeDifferenceSeconds = serverTime - currentTime
            aligned = true
        } catch (e: Exception) {
            // Matches desktop behavior: on failure, just keep using unadjusted device time.
        }
    }
}
