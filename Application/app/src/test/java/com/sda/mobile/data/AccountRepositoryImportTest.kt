package com.sda.mobile.data

import com.sda.mobile.model.SteamGuardAccount
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class AccountRepositoryImportTest {
    @Test
    fun desktopStyleMaFileDeserializes() {
        val source = """
            {
              "shared_secret": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
              "serial_number": "00000000000000000000",
              "revocation_code": "R00000",
              "uri": "otpauth://totp/Steam:synthetic-account?secret=AEBAOCAJBIFQYDIOB4IBCEQTCQKRMFYY&issuer=Steam",
              "server_time": 1700000000,
              "account_name": "synthetic-account",
              "token_gid": "000000000000000",
              "identity_secret": "ERERERERERERERERERERERERERE=",
              "secret_1": "IiIiIiIiIiIiIiIiIiIiIiIiIiI=",
              "status": 1,
              "device_id": "android:00000000-0000-0000-0000-000000000000",
              "phone_number_hint": null,
              "confirm_type": 3,
              "fully_enrolled": true,
              "Session": {
                "SteamID": 12345678901234567,
                "AccessToken": "synthetic-access-token",
                "RefreshToken": "synthetic-refresh-token",
                "SessionID": null
              }
            }
        """.trimIndent()

        val account = Json { ignoreUnknownKeys = true }.decodeFromString(SteamGuardAccount.serializer(), source)

        assertNotNull(account)
        assertEquals(12345678901234567L, account.steamId64)
        assertEquals("synthetic-account", account.accountName)
        assertEquals("synthetic-access-token", account.session?.accessToken)
    }
}
