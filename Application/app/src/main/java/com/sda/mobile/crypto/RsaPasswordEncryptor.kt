package com.sda.mobile.crypto

import android.util.Base64
import java.math.BigInteger
import java.security.KeyFactory
import java.security.spec.RSAPublicKeySpec
import javax.crypto.Cipher

/**
 * Encrypts the plaintext password for Steam's HTTP login handshake:
 * IAuthenticationService/GetPasswordRSAPublicKey returns a per-attempt RSA public key
 * (modulus/exponent as hex strings); the password is RSA/PKCS1-encrypted with that key and
 * base64-sent to BeginAuthSessionViaCredentials along with the timestamp the key was issued
 * with. This matches the encryption step SteamKit2's CredentialsAuthSession performs
 * internally on desktop - see auth/LoginClient.kt for the full flow.
 */
object RsaPasswordEncryptor {
    fun encryptPassword(password: String, modulusHex: String, exponentHex: String): String {
        val modulus = BigInteger(modulusHex, 16)
        val exponent = BigInteger(exponentHex, 16)
        val keyFactory = KeyFactory.getInstance("RSA")
        val publicKey = keyFactory.generatePublic(RSAPublicKeySpec(modulus, exponent))

        val cipher = Cipher.getInstance("RSA/ECB/PKCS1Padding")
        cipher.init(Cipher.ENCRYPT_MODE, publicKey)
        val encrypted = cipher.doFinal(password.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(encrypted, Base64.NO_WRAP)
    }
}
