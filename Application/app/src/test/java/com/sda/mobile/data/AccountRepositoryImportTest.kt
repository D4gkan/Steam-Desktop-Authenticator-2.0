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
              "shared_secret": "MNL9kC3GkO7QkNLBB5WqbaARSGQ=",
              "serial_number": "10400669811768428385",
              "revocation_code": "R75778",
              "uri": "otpauth://totp/Steam:liorabloom?secret=GDJP3EBNY2IO5UEQ2LAQPFNKNWQBCSDE&issuer=Steam",
              "server_time": 1787482886,
              "account_name": "liorabloom",
              "token_gid": "5668ae813f03f29",
              "identity_secret": "7c6V6RvjW6c5RYNJcU6jd9JN9aM=",
              "secret_1": "pv+52p4w8/0oTz3SpZo3EHwDe18=",
              "status": 1,
              "device_id": "android:b590d140-6b21-4776-8d40-68d413882f9f",
              "phone_number_hint": null,
              "confirm_type": 3,
              "fully_enrolled": true,
              "Session": {
                "SteamID": 76561198654953753,
                "AccessToken": "abc",
                "RefreshToken": "def",
                "SessionID": null
              }
            }
        """.trimIndent()

        val account = Json { ignoreUnknownKeys = true }.decodeFromString(SteamGuardAccount.serializer(), source)

        assertNotNull(account)
        assertEquals(76561198654953753L, account.steamId64)
        assertEquals("liorabloom", account.accountName)
        assertEquals("abc", account.session?.accessToken)
    }
}
