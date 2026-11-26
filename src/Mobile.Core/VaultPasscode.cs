using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Sda.Mobile.Core;

public interface IVaultSecurityStore
{
    string? ReadSecurityValue(string name);
    void WriteSecurityValue(string name, string value);
    void RemoveSecurityValue(string name);
}

/// <summary>Wraps the existing vault key without changing account-file formats.</summary>
public sealed class VaultPasscode
{
    public const string StateName = "passcode-v1";
    private const string DeviceSecretName = "passcode-device-secret-v1";
    private const int Iterations = 210_000;
    private static readonly byte[] AssociatedData = "SDA.Mobile.Vault.Passcode.v1"u8.ToArray();
    private readonly IVaultSecurityStore _store;
    private readonly TimeProvider _clock;

    public VaultPasscode(IVaultSecurityStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsConfigured => _store.ReadSecurityValue(StateName) != null;
    public int? DigitCount => IsConfigured && ReadState().Digits is 6 or 8 ? ReadState().Digits : null;

    public static void Validate(string passcode)
    {
        if (passcode.Length is not (6 or 8) || passcode.Any(character => character is < '0' or > '9'))
            throw new InvalidOperationException("Choose a passcode with exactly 6 or 8 digits.");
    }

    public void Create(string passcode, string vaultKey)
    {
        if (IsConfigured) throw new InvalidOperationException("A vault passcode is already configured.");
        Protect(passcode, vaultKey);
    }

    public void Change(string currentPasscode, string newPasscode)
    {
        Validate(newPasscode);
        var vaultKey = Unlock(currentPasscode);
        Protect(newPasscode, vaultKey);
    }

    public string Unlock(string passcode)
    {
        var state = ReadState();
        var remaining = state.RetryAfter - _clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
            throw new InvalidOperationException($"Too many attempts. Try again in {(int)Math.Ceiling(remaining.TotalSeconds)} seconds.");
        Validate(passcode);
        var deviceSecret = _store.ReadSecurityValue(DeviceSecretName)
            ?? throw new InvalidDataException("The device key is missing. Restore your encrypted backup; the vault has not been changed.");
        var key = DeriveKey(passcode, deviceSecret, Convert.FromBase64String(state.Salt));
        var plaintext = new byte[Convert.FromBase64String(state.Ciphertext).Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(state.Nonce), Convert.FromBase64String(state.Ciphertext),
                Convert.FromBase64String(state.Tag), plaintext, AssociatedData);
            state.FailedAttempts = 0;
            state.RetryAfter = default;
            state.Digits = passcode.Length;
            WriteState(state);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            state.FailedAttempts = Math.Min(state.FailedAttempts + 1, 100);
            if (state.FailedAttempts >= 5)
                state.RetryAfter = _clock.GetUtcNow().AddSeconds(Math.Min(900, 30 * Math.Pow(2, Math.Min(5, state.FailedAttempts - 5))));
            WriteState(state);
            throw new InvalidOperationException(state.FailedAttempts >= 5
                ? "Incorrect passcode. Too many attempts; wait before trying again."
                : "Incorrect passcode. Try again.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void Protect(string passcode, string vaultKey)
    {
        Validate(passcode);
        if (string.IsNullOrEmpty(vaultKey)) throw new ArgumentException("A vault key is required.", nameof(vaultKey));
        var deviceSecret = _store.ReadSecurityValue(DeviceSecretName);
        if (deviceSecret == null)
        {
            deviceSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _store.WriteSecurityValue(DeviceSecretName, deviceSecret);
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(vaultKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var key = DeriveKey(passcode, deviceSecret, salt);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            WriteState(new State
            {
                Salt = Convert.ToBase64String(salt), Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext), Tag = Convert.ToBase64String(tag), Digits = passcode.Length
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveKey(string passcode, string deviceSecret, byte[] salt)
    {
        var secret = Convert.FromBase64String(deviceSecret);
        var input = Encoding.UTF8.GetBytes(passcode);
        var combined = HMACSHA256.HashData(secret, input);
        try { return Rfc2898DeriveBytes.Pbkdf2(combined, salt, Iterations, HashAlgorithmName.SHA256, 32); }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    private State ReadState()
    {
        var state = JsonConvert.DeserializeObject<State>(_store.ReadSecurityValue(StateName)
            ?? throw new InvalidOperationException("Set up a vault passcode first."));
        if (state == null || state.Version != 1 || Convert.FromBase64String(state.Salt).Length != 16 ||
            Convert.FromBase64String(state.Nonce).Length != 12 || Convert.FromBase64String(state.Tag).Length != 16 ||
            Convert.FromBase64String(state.Ciphertext).Length is < 1 or > 256 || state.FailedAttempts < 0 || state.Digits is not (0 or 6 or 8))
            throw new InvalidDataException("The vault protection settings are damaged. Keep your backup; the vault has not been changed.");
        return state;
    }

    private void WriteState(State state) => _store.WriteSecurityValue(StateName, JsonConvert.SerializeObject(state));

    private sealed class State
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Ciphertext { get; set; } = "";
        public string Tag { get; set; } = "";
        public int FailedAttempts { get; set; }
        public int Digits { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
    }
}
