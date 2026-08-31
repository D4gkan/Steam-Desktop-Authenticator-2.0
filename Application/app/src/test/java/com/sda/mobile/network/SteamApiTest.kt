package com.sda.mobile.network

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.assertEquals
import org.junit.Test

class SteamApiTest {
    @Test
    fun parseConfirmationFailure_usesFriendlyMessageForExpiredSession() {
        val obj = Json.parseToJsonElement("""
            {"success":false,"needauth":true,"message":"Needs Authentication"}
        """).jsonObject

        assertEquals("Session expired. Please log in again.", SteamApi().parseConfirmationFailure(obj))
    }

    @Test
    fun parseConfirmationFailure_usesFallbackWhenMessageMissing() {
        val obj = Json.parseToJsonElement("""
            {"success":false}
        """).jsonObject

        assertEquals("Steam returned an error while fetching confirmations.", SteamApi().parseConfirmationFailure(obj))
    }
}
