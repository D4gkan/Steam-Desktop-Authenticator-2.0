package com.sda.mobile.network

import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.FormBody
import okhttp3.HttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.concurrent.TimeUnit

/**
 * Matches SteamAuth.SteamWeb on desktop: same mobile-app user agent string, plain GET/POST
 * helpers, and (for confirmation calls) the same steamLoginSecure/sessionid/mobileClient
 * cookie set that SessionData.GetCookies() builds there.
 */
object SteamHttpClient {
    const val MOBILE_APP_USER_AGENT = "Dalvik/2.1.0 (Linux; U; Android 9; Valve Steam App Version/3)"

    /** In-memory cookie jar, keyed per SteamGuardAccount by the caller (a fresh jar per
     * account/session avoids cross-account cookie bleed). */
    class SessionCookieJar : CookieJar {
        private val store = mutableMapOf<String, MutableList<Cookie>>()

        fun setForDomains(domains: List<String>, name: String, value: String) {
            for (domain in domains) {
                val cookie = Cookie.Builder()
                    .name(name)
                    .value(value)
                    .domain(domain)
                    .path("/")
                    .build()
                store.getOrPut(domain) { mutableListOf() }.removeAll { it.name == name }
                store.getOrPut(domain) { mutableListOf() }.add(cookie)
            }
        }

        override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
            store.getOrPut(url.host) { mutableListOf() }.apply {
                for (c in cookies) {
                    removeAll { it.name == c.name }
                    add(c)
                }
            }
        }

        override fun loadForRequest(url: HttpUrl): List<Cookie> = store[url.host] ?: emptyList()
    }

    fun newClient(cookieJar: CookieJar? = null): OkHttpClient =
        OkHttpClient.Builder()
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .apply { if (cookieJar != null) cookieJar(cookieJar) }
            .build()

    suspend fun get(client: OkHttpClient, url: String): String {
        val request = Request.Builder()
            .url(url)
            .header("User-Agent", MOBILE_APP_USER_AGENT)
            .header("Accept", "application/json")
            .header("Origin", "https://steamcommunity.com")
            .header("Referer", "https://steamcommunity.com/")
            .get()
            .build()
        return execute(client, request)
    }

    suspend fun post(client: OkHttpClient, url: String, form: Map<String, String>): String {
        val bodyBuilder = FormBody.Builder()
        for ((k, v) in form) bodyBuilder.add(k, v)
        val request = Request.Builder()
            .url(url)
            .header("User-Agent", MOBILE_APP_USER_AGENT)
            .header("Accept", "application/json")
            .header("Origin", "https://steamcommunity.com")
            .header("Referer", "https://steamcommunity.com/")
            .header("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8")
            .post(bodyBuilder.build())
            .build()
        return execute(client, request)
    }

    suspend fun postRaw(client: OkHttpClient, url: String, body: String, contentType: String): String {
        val request = Request.Builder()
            .url(url)
            .header("User-Agent", MOBILE_APP_USER_AGENT)
            .header("Accept", "application/json")
            .header("Origin", "https://steamcommunity.com")
            .header("Referer", "https://steamcommunity.com/")
            .header("Content-Type", contentType)
            .post(body.toRequestBody(contentType.toMediaType()))
            .build()
        return execute(client, request)
    }

    private suspend fun execute(client: OkHttpClient, request: Request): String =
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            client.newCall(request).execute().use { response ->
                response.body?.string() ?: throw java.io.IOException("Empty response body from ${request.url}")
            }
        }
}
