package com.sda.mobile.data

import com.sda.mobile.model.SessionData
import com.sda.mobile.model.SteamGuardAccount
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class QrPayloadCodecTest {
    @Test
    fun encodedPayloadRoundTripsUnicodeJson() {
        val plaintext = """{"account_name":"اختبار 🎮","AccessToken":"${"token.".repeat(200)}"}"""

        val encoded = QrPayloadCodec.encode(plaintext)

        assertTrue(encoded.startsWith(QrPayloadCodec.PREFIX))
        assertFalse(encoded.contains("account_name"))
        assertEquals(plaintext, QrPayloadCodec.decodeIfEncoded(encoded))
    }

    @Test
    fun legacyPlaintextPayloadIsStillAccepted() {
        val plaintext = "  {\"account_name\":\"legacy\"}  "

        assertEquals(plaintext.trim(), QrPayloadCodec.decodeIfEncoded(plaintext))
    }

    @Test(expected = IllegalArgumentException::class)
    fun malformedVersionOnePayloadIsRejected() {
        QrPayloadCodec.decodeIfEncoded(QrPayloadCodec.PREFIX + "not-gzip")
    }

    @Test
    fun qrExportKeepsSteamIdButDropsLargeSessionCredentials() {
        val account = SteamGuardAccount(
            sharedSecret = "shared",
            session = SessionData(
                steamId = 76561198000000000L,
                accessToken = "large-access-token",
                refreshToken = "large-refresh-token",
                sessionId = "session-id"
            )
        )

        val qrAccount = account.withoutQrSessionCredentials()

        assertEquals(76561198000000000L, qrAccount.steamId64)
        assertEquals("shared", qrAccount.sharedSecret)
        assertEquals(null, qrAccount.session?.accessToken)
        assertEquals(null, qrAccount.session?.refreshToken)
        assertEquals(null, qrAccount.session?.sessionId)
    }
}
