# SDA Mobile (Android)

A native Android companion to Steam Desktop Authenticator (Kotlin + Jetpack Compose). It reads
and writes the same `.maFile` / `manifest.json` format as the desktop app, so accounts move
freely between the two - via file transfer or the QR export/import feature described below.

## ⚠️ Read this first

This project was written in a sandboxed build environment with **no access to the Android SDK,
Google's Maven repository, or `api.steampowered.com`**. That means:

- **It has not been compiled.** Every file has been carefully reviewed by hand for correctness,
  and the core crypto (TOTP codes, confirmation hashing, AES/PBKDF2 file encryption) is a
  line-by-line port of the desktop app's own algorithms - but Gradle has never actually run
  against this code. Expect to fix minor compile errors (an import, a type mismatch) on your
  first build.
- **The login flow has not been exercised against real Steam.** The desktop app logs in via
  SteamKit2, a .NET-only library that speaks Steam's binary CM-socket protocol - not something
  this Android app can use. Instead, `auth/LoginClient.kt` talks directly to the same
  `IAuthenticationService` HTTPS endpoints SteamKit2 itself calls underneath
  (`GetPasswordRSAPublicKey` → `BeginAuthSessionViaCredentials` →
  `UpdateAuthSessionWithSteamGuardCode` → `PollAuthSessionStatus`). This is a well-established
  approach (other non-.NET Steam login implementations use the same endpoints), and the
  request/response shapes were reconstructed carefully, but they haven't been tested live.
  **Test login and the "add a new authenticator" flow with a throwaway/alt Steam account
  first.** Everything downstream of login (TOTP codes, confirmations, the `ITwoFactorService`
  and `IPhoneService` calls) is a direct port of code the desktop app already runs in
  production, so it's on much firmer ground.
- **The Gradle wrapper jar isn't included** (it's a binary this environment couldn't fetch).
  Run `gradle wrapper` once with a local Gradle install (or just open the project in Android
  Studio, which bootstraps its own wrapper) before using `./gradlew`.
- **No app icon/branding** - there's a simple placeholder vector icon. Swap
  `app/src/main/res/drawable/ic_launcher_foreground.xml` (and `_background.xml`) for real
  artwork whenever you like.

None of this should block you - it just means budget an evening for first-build debugging
rather than expecting a one-shot `./gradlew assembleDebug`.

## Building

1. Install Android Studio (or just the command-line SDK + JDK 17).
2. From the repository root, run `cd Application && gradle wrapper` (only needed once, to
   generate `gradlew`/`gradlew.bat` - skip this if you're opening the project in Android
   Studio instead).
3. `./gradlew assembleDebug` (or **Run** in Android Studio). The debug APK lands in
   `app/build/outputs/apk/debug/`.
4. Install on your phone: `adb install app/build/outputs/apk/debug/app-debug.apk`, or copy the
   APK over and install it directly (enable "install unknown apps" for whatever app you use to
   open it).

There's no Play Store listing - this is a sideloaded app, same spirit as running the desktop
build straight from source.

## Architecture

```
crypto/     Pure algorithms: TOTP codes, confirmation hashing, AES/PBKDF2 file encryption,
            RSA password encryption for login. Byte-for-byte ports of ../src/SteamAuth/*.cs
            and ../src/App/Core/FileEncryptor.cs from the desktop app - this is what keeps
            .maFiles interchangeable between the two apps.
model/      Data classes mirroring the desktop app's JSON shapes exactly (field names, casing).
network/    HTTP client + all the Steam Web API endpoints (confirmations, authenticator
            add/remove/finalize, phone linking, time sync).
auth/       LoginClient (the HTTPS-only login handshake - see the warning above) and
            AuthenticatorLinker (the add-authenticator/phone-linking state machine, ported from
            AuthenticatorLinker.cs).
data/       Local storage: AccountRepository (manifest.json + .maFile, encryption - ported from
            ../src/App/Core/Manifest.cs), CredentialStore (Android Keystore-backed saved-password
            store, the mobile equivalent of the desktop app's ICredentialStore implementations),
            UiMetaRepository (per-account display metadata), ConfirmationRepository.
ui/         Jetpack Compose screens, ViewModels, navigation, and theme.
```

## Security notes

- Accounts are stored in this app's private storage (`Context.filesDir/maFiles/`), which is
  sandboxed to this app by Android and excluded from backups
  (`android:allowBackup="false"` in the manifest) - nothing else on the device can read it
  without root.
- The optional encryption passkey (same feature as desktop) is never written to disk - it's
  held in memory only, for the lifetime of the app process.
- Saved passwords (for automatic session refresh) go into `EncryptedSharedPreferences`, backed
  by a key that lives in the Android Keystore (hardware-backed on most devices) - this is the
  Android-native equivalent of Windows Credential Manager / macOS Keychain / the Linux Secret
  Service on desktop.
- **QR export is plaintext by design** - the QR code has to be independently decodable, so it
  can't carry the app's own encryption. Both the desktop's "Export as QR…" and the mobile
  app's "Export as QR code" screen say this loudly. Treat the QR code itself as sensitive for
  as long as it's on screen.

## What's *not* in this build

- **No background polling / push notifications for confirmations**, per the intended design -
  the app checks for confirmations only when you open the Confirmations screen (or pull to
  refresh). This avoids needing a persistent foreground service and its battery/OS-restriction
  complications.
- No automated tests. Given the "hasn't touched real Steam yet" caveat above, the highest-value
  first testing pass is manual: import an existing .maFile and confirm codes match the desktop
  app for the same account, then (with a throwaway account) walk the full login/link flow.
