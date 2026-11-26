using Newtonsoft.Json.Linq;
using Sda.Mobile.Core;
using Xunit;

namespace Sda.Mobile.Tests;

public sealed class VaultPasscodeTests
{
    internal sealed class MemoryStore : IVaultSecurityStore
    {
        internal Dictionary<string, string> Values { get; } = new();
        public string? ReadSecurityValue(string name) => Values.GetValueOrDefault(name);
        public void WriteSecurityValue(string name, string value) => Values[name] = value;
        public void RemoveSecurityValue(string name) => Values.Remove(name);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("01234567")]
    public void PasscodeProtectsTheExistingVaultKeyAcrossInstances(string pin)
    {
        var store = new MemoryStore();
        new VaultPasscode(store).Create(pin, "synthetic vault key");
        Assert.DoesNotContain("synthetic vault key", store.ReadSecurityValue(VaultPasscode.StateName));
        Assert.DoesNotContain(pin, store.ReadSecurityValue(VaultPasscode.StateName));
        Assert.Equal("synthetic vault key", new VaultPasscode(store).Unlock(pin));
        Assert.Throws<InvalidOperationException>(() => new VaultPasscode(store).Unlock("999999"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("12345a")]
    [InlineData("１２３４５６")]
    public void RejectsAnythingExceptSixOrEightAsciiDigits(string pin)
        => Assert.Throws<InvalidOperationException>(() => VaultPasscode.Validate(pin));

    [Fact]
    public void PasscodeChangeKeepsTheVaultKeyAndRejectsTheOldPasscode()
    {
        var vault = new VaultPasscode(new MemoryStore());
        vault.Create("123456", "synthetic key");
        Assert.Throws<InvalidOperationException>(() => vault.Change("000000", "12345678"));
        Assert.Equal("synthetic key", vault.Unlock("123456"));
        vault.Change("123456", "12345678");
        Assert.Throws<InvalidOperationException>(() => vault.Unlock("123456"));
        Assert.Equal("synthetic key", vault.Unlock("12345678"));
    }

    [Fact]
    public void LockoutSurvivesServiceRecreationAndSuccessfulUnlockResetsIt()
    {
        var store = new MemoryStore();
        var clock = new TestClock();
        new VaultPasscode(store, clock).Create("123456", "synthetic key");
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Throws<InvalidOperationException>(() => new VaultPasscode(store, clock).Unlock("000000"));
        Assert.Contains("30 seconds", Assert.Throws<InvalidOperationException>(() => new VaultPasscode(store, clock).Unlock("123456")).Message);
        clock.Now = clock.Now.AddSeconds(31);
        Assert.Equal("synthetic key", new VaultPasscode(store, clock).Unlock("123456"));
        Assert.Throws<InvalidOperationException>(() => new VaultPasscode(store, clock).Unlock("000000"));
        Assert.Equal("synthetic key", new VaultPasscode(store, clock).Unlock("123456"));
    }

    [Fact]
    public void TamperedWrapperOrMissingDeviceSecretCannotUnlockTheKey()
    {
        var store = new MemoryStore();
        var vault = new VaultPasscode(store);
        vault.Create("123456", "synthetic key");
        var original = store.ReadSecurityValue(VaultPasscode.StateName)!;
        var state = JObject.Parse(original);
        state["Tag"] = Convert.ToBase64String(new byte[16]);
        store.WriteSecurityValue(VaultPasscode.StateName, state.ToString());
        Assert.Throws<InvalidOperationException>(() => vault.Unlock("123456"));
        store.WriteSecurityValue(VaultPasscode.StateName, original);
        store.Values.Remove("passcode-device-secret-v1");
        Assert.Throws<InvalidDataException>(() => vault.Unlock("123456"));
        Assert.Equal(original, store.ReadSecurityValue(VaultPasscode.StateName));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("12345678")]
    public void ExistingPasscodeWithoutLengthMetadataMigratesAfterSuccessfulUnlock(string pin)
    {
        var store = new MemoryStore();
        var vault = new VaultPasscode(store);
        vault.Create(pin, "synthetic key");
        var state = JObject.Parse(store.ReadSecurityValue(VaultPasscode.StateName)!);
        state.Remove("Digits");
        store.WriteSecurityValue(VaultPasscode.StateName, state.ToString());
        Assert.Null(vault.DigitCount);
        Assert.Equal("synthetic key", vault.Unlock(pin));
        Assert.Equal(pin.Length, vault.DigitCount);
        Assert.Equal(state["Ciphertext"], JObject.Parse(store.ReadSecurityValue(VaultPasscode.StateName)!)["Ciphertext"]);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
