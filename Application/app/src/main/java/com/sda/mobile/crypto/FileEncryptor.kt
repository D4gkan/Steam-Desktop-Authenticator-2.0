package com.sda.mobile.crypto

import android.util.Base64
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/**
 * Port of the desktop app's Core/FileEncryptor.cs. Same on-disk format, so a .maFile encrypted
 * by this app decrypts correctly in the desktop app and vice versa:
 *   - PBKDF2 (RFC2898/PBKDF2WithHmacSHA1), 50,000 iterations, 32-byte derived key
 *   - AES-256, CBC mode, PKCS7 (PKCS5, byte-identical for a 16-byte block) padding
 *   - 8-byte random salt, 16-byte random IV, both base64-encoded and stored per-entry
 */
object FileEncryptor {
    private const val PBKDF2_ITERATIONS = 50000
    private const val SALT_LENGTH_BYTES = 8
    private const val KEY_SIZE_BITS = 256
    private const val IV_LENGTH_BYTES = 16

    private val secureRandom = SecureRandom()

    fun getRandomSalt(): String {
        val salt = ByteArray(SALT_LENGTH_BYTES)
        secureRandom.nextBytes(salt)
        return Base64.encodeToString(salt, Base64.NO_WRAP)
    }

    fun getInitializationVector(): String {
        val iv = ByteArray(IV_LENGTH_BYTES)
        secureRandom.nextBytes(iv)
        return Base64.encodeToString(iv, Base64.NO_WRAP)
    }

    private fun getEncryptionKey(password: String, saltBase64: String): ByteArray {
        require(password.isNotEmpty()) { "Password is empty" }
        require(saltBase64.isNotEmpty()) { "Salt is empty" }

        val salt = Base64.decode(saltBase64, Base64.DEFAULT)
        val spec = PBEKeySpec(password.toCharArray(), salt, PBKDF2_ITERATIONS, KEY_SIZE_BITS)
        val factory = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA1")
        return factory.generateSecret(spec).encoded
    }

    /** Returns null on a bad passkey (padding/MAC failure), matching the desktop behavior. */
    fun decryptData(password: String, passwordSalt: String, iv: String, encryptedData: String): String? {
        require(password.isNotEmpty()) { "Password is empty" }
        require(passwordSalt.isNotEmpty()) { "Salt is empty" }
        require(iv.isNotEmpty()) { "Initialization Vector is empty" }
        require(encryptedData.isNotEmpty()) { "Encrypted data is empty" }

        val cipherText = Base64.decode(encryptedData, Base64.DEFAULT)
        val key = getEncryptionKey(password, passwordSalt)

        return try {
            val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
            cipher.init(
                Cipher.DECRYPT_MODE,
                SecretKeySpec(key, "AES"),
                IvParameterSpec(Base64.decode(iv, Base64.DEFAULT))
            )
            String(cipher.doFinal(cipherText), Charsets.UTF_8)
        } catch (e: Exception) {
            null
        }
    }

    fun encryptData(password: String, passwordSalt: String, iv: String, plaintext: String): String {
        require(password.isNotEmpty()) { "Password is empty" }
        require(passwordSalt.isNotEmpty()) { "Salt is empty" }
        require(iv.isNotEmpty()) { "Initialization Vector is empty" }
        require(plaintext.isNotEmpty()) { "Plaintext data is empty" }

        val key = getEncryptionKey(password, passwordSalt)
        val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
        cipher.init(
            Cipher.ENCRYPT_MODE,
            SecretKeySpec(key, "AES"),
            IvParameterSpec(Base64.decode(iv, Base64.DEFAULT))
        )
        return Base64.encodeToString(cipher.doFinal(plaintext.toByteArray(Charsets.UTF_8)), Base64.NO_WRAP)
    }
}
