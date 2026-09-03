package com.sda.mobile.data

import com.sda.mobile.network.ApiEndpoints
import com.sda.mobile.network.SteamHttpClient
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import okhttp3.OkHttpClient

/**
 * Resolves the Steam profile-picture URL for a SteamID from the public profile XML feed (no
 * Web API key required - see ApiEndpoints.profileXml). This only ever returns a URL string;
 * actual image download + on-disk caching is left to Coil (AsyncImage in AccountListScreen),
 * which already has its own disk/memory cache, so this repository doesn't duplicate that.
 *
 * Entirely best-effort: any failure (offline, Steam unavailable, malformed feed) results in a
 * null URL, never an exception, so callers never need error handling and the account list
 * always falls back to its default placeholder avatar.
 */
class AvatarRepository {
    private val client: OkHttpClient = SteamHttpClient.newClient()
    private val mutex = Mutex()
    private val resolvedUrls = mutableMapOf<Long, String?>()

    /** Returns a cached URL immediately if already resolved this session, without a network
     * call. Null means "not yet resolved" - call [resolve] to fetch it. */
    fun getCached(steamId64: Long): String? = resolvedUrls[steamId64]

    /** Resolves (and memoizes) the avatar URL for a SteamID. Safe to call repeatedly and
     * concurrently for the same account; never throws. */
    suspend fun resolve(steamId64: Long): String? {
        resolvedUrls[steamId64]?.let { return it }
        if (resolvedUrls.containsKey(steamId64)) return null // previously resolved to "no avatar"

        return mutex.withLock {
            // Re-check inside the lock in case another coroutine resolved it while we waited.
            if (resolvedUrls.containsKey(steamId64)) return@withLock resolvedUrls[steamId64]

            val url = try {
                val xml = SteamHttpClient.get(client, ApiEndpoints.profileXml(steamId64))
                extractAvatarFullUrl(xml)
            } catch (e: Exception) {
                null // offline, timeout, malformed profile, etc. - never fail account loading
            }

            resolvedUrls[steamId64] = url
            url
        }
    }

    companion object {
        private val AVATAR_CDATA_REGEX = Regex("<avatarFull><!\\[CDATA\\[(.*?)]]></avatarFull>")
        private val AVATAR_PLAIN_REGEX = Regex("<avatarFull>(https?://[^<]+)</avatarFull>")

        internal fun extractAvatarFullUrl(xml: String): String? {
            AVATAR_CDATA_REGEX.find(xml)?.let { return it.groupValues[1] }
            AVATAR_PLAIN_REGEX.find(xml)?.let { return it.groupValues[1] }
            return null
        }
    }
}
