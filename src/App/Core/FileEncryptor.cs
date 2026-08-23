using System;
using System.IO;
using System.Security.Cryptography;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>
    /// Ported 1:1 from the original SDA FileEncryptor.cs. Behavior and on-disk format are
    /// unchanged so existing .maFile / manifest.json data remains fully compatible:
    ///   - PBKDF2 (RFC2898), 50,000 iterations, 32-byte derived key
    ///   - AES-256, CBC mode, PKCS7 padding
    ///   - 8-byte random salt, 16-byte random IV, both base64-encoded and stored per-entry
    ///
    /// The only change from the original is using System.Security.Cryptography.Aes.Create()
    /// instead of the obsolete RijndaelManaged class, which is not available outside .NET
    /// Framework/Windows. With IV length 16 and key length 32, Rijndael and AES are byte-for-byte
    /// identical algorithms, so this does not change the format or break existing files.
    /// </summary>
    public static class FileEncryptor
    {
        private const int PBKDF2_ITERATIONS = 50000;
        private const int SALT_LENGTH = 8;
        private const int KEY_SIZE_BYTES = 32;
        private const int IV_LENGTH = 16;

        public static string GetRandomSalt()
        {
            byte[] salt = new byte[SALT_LENGTH];
            RandomNumberGenerator.Fill(salt);
            return Convert.ToBase64String(salt);
        }

        public static string GetInitializationVector()
        {
            byte[] iv = new byte[IV_LENGTH];
            RandomNumberGenerator.Fill(iv);
            return Convert.ToBase64String(iv);
        }

        private static byte[] GetEncryptionKey(string password, string salt)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is empty");
            if (string.IsNullOrEmpty(salt)) throw new ArgumentException("Salt is empty");

            using var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), PBKDF2_ITERATIONS, HashAlgorithmName.SHA1);
            return pbkdf2.GetBytes(KEY_SIZE_BYTES);
        }

        public static string? DecryptData(string password, string passwordSalt, string iv, string encryptedData)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is empty");
            if (string.IsNullOrEmpty(passwordSalt)) throw new ArgumentException("Salt is empty");
            if (string.IsNullOrEmpty(iv)) throw new ArgumentException("Initialization Vector is empty");
            if (string.IsNullOrEmpty(encryptedData)) throw new ArgumentException("Encrypted data is empty");

            byte[] cipherText = Convert.FromBase64String(encryptedData);
            byte[] key = GetEncryptionKey(password, passwordSalt);

            using var aes = Aes.Create();
            aes.IV = Convert.FromBase64String(iv);
            aes.Key = key;
            aes.Padding = PaddingMode.PKCS7;
            aes.Mode = CipherMode.CBC;

            try
            {
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var msDecrypt = new MemoryStream(cipherText);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                return srDecrypt.ReadToEnd();
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        public static string EncryptData(string password, string passwordSalt, string iv, string plaintext)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is empty");
            if (string.IsNullOrEmpty(passwordSalt)) throw new ArgumentException("Salt is empty");
            if (string.IsNullOrEmpty(iv)) throw new ArgumentException("Initialization Vector is empty");
            if (string.IsNullOrEmpty(plaintext)) throw new ArgumentException("Plaintext data is empty");

            byte[] key = GetEncryptionKey(password, passwordSalt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = Convert.FromBase64String(iv);
            aes.Padding = PaddingMode.PKCS7;
            aes.Mode = CipherMode.CBC;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var msEncrypt = new MemoryStream();
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plaintext);
            }
            return Convert.ToBase64String(msEncrypt.ToArray());
        }
    }
}
