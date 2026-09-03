# Codex Security Review — 2026-09-03

## Executive summary

Codex Security completed a whole-repository static review of commit
`83adc78e5fd196eda378f50def7d6f55c68586e2`. The review found 11 reportable
issues: 1 critical, 1 high, 6 medium, and 3 low.

The most urgent issue is an incident, not only a code defect: tracked build
output contains plaintext Steam authenticator and session material. The
Android release-signing keystore and its usable credentials are also tracked
together. This document intentionally does not reproduce any secret values or
the account identifiers contained in the affected files.

## Immediate response

Before treating code changes as sufficient remediation:

1. Treat every account represented by the tracked `.maFile` records as
   compromised. Revoke sessions, remove and re-enrol each authenticator,
   rotate recovery material and passwords where appropriate, and review trade
   and market history.
2. Treat the Android signing key as compromised. Use the distribution
   channel's signing-key upgrade or compromised-key process and verify the
   certificate fingerprints of published builds.
3. Remove the sensitive files from the current tree, purge them from every
   intended reachable Git ref and distributed artifact, and notify known clone
   and fork recipients.
4. Add CI secret scanning and reject `.maFile` data, keystores, embedded
   signing credentials, and tracked build output.

## Findings

### 1. Plaintext Steam authenticator and session credentials

- **Severity:** Critical
- **Confidence:** High
- **Locations:** tracked files under
  `src/App/bin/Debug/net8.0/maFiles/`, the fixture in
  `Application/app/src/test/java/com/sda/mobile/data/AccountRepositoryImportTest.kt`,
  and the consumers in `src/SteamAuth/SessionData.cs` and
  `src/SteamAuth/SteamGuardAccount.cs`.
- **Evidence:** eight tracked `.maFile` records contain non-empty
  authenticator, recovery, and reusable session fields. The tracked manifest
  declares plaintext storage. Refresh-token claims appear current, although
  Steam-side revocation was not tested.
- **Impact:** repository readers can obtain code-generation, confirmation,
  session-refresh, and authenticator-removal authority.
- **Remediation:** complete the incident-response actions above, replace the
  fixture with synthetic non-secret data, and add history and CI secret scans.

### 2. Android release-signing key and credentials are committed together

- **Severity:** High
- **Confidence:** High
- **Locations:** `Application/sda-release-key.jks` and
  `Application/app/build.gradle.kts`.
- **Evidence:** the release build selects the tracked JKS, the Gradle file
  supplies both required credentials, and offline validation confirmed a
  usable private-key entry. No credential values are reproduced here.
- **Impact:** a repository reader has the material needed to sign a malicious
  APK under the configured package identity; exploitation still requires a
  release, update, sideload, or social-engineering path.
- **Remediation:** rotate the signing identity through the applicable store or
  distribution process, purge the old material from Git, and inject replacement
  signing material only from protected CI or hardware-backed infrastructure.

### 3. Legacy PBKDF2 parameters permit inexpensive offline guessing

- **Severity:** Medium
- **Confidence:** High
- **Locations:** `src/App/Core/FileEncryptor.cs`, `src/App/Core/Manifest.cs`,
  `Application/app/src/main/java/com/sda/mobile/crypto/FileEncryptor.kt`, and
  `Application/app/src/main/java/com/sda/mobile/data/AccountRepository.kt`.
- **Evidence:** both clients use PBKDF2-HMAC-SHA1 with 50,000 iterations and an
  eight-byte salt. A stolen record gives an attacker a local guess-verification
  oracle.
- **Remediation:** introduce a versioned AEAD envelope using a calibrated
  memory-hard KDF such as Argon2id or scrypt and at least a 16-byte random salt;
  migrate legacy records after successful unlock.

### 4. Encrypted `.maFile` records accept undetected CBC tampering

- **Severity:** Low
- **Confidence:** High
- **Locations:** `src/App/Core/FileEncryptor.cs`, `src/App/Core/Manifest.cs`,
  `Application/app/src/main/java/com/sda/mobile/crypto/FileEncryptor.kt`, and
  `Application/app/src/main/java/com/sda/mobile/data/AccountRepository.kt`.
- **Evidence:** both clients use AES-CBC with mutable IV metadata and no MAC or
  AEAD tag.
- **Remediation:** migrate to AES-GCM or ChaCha20-Poly1305 and authenticate the
  format version, SteamID, filename, and other binding metadata before parsing.

### 5. Portable desktop storage can be broadly readable

- **Severity:** Medium
- **Confidence:** Medium
- **Locations:** `src/App/Core/Manifest.cs`, `src/App/Core/Logger.cs`, and
  `src/App/Program.cs`.
- **Evidence:** desktop secrets, manifests, and logs are written beneath
  `AppContext.BaseDirectory` without explicit owner-only Unix modes or a
  current-user-only Windows ACL.
- **Remediation:** prefer the platform's per-user application-data directory,
  create directories as `0700` and files as `0600` on Unix, apply a restrictive
  DACL on Windows, and audit existing installations.

### 6. Account removal leaves a saved-password alias

- **Severity:** Medium
- **Confidence:** High
- **Locations:** `src/App/Core/Security/CredentialStoreCompat.cs` and
  `src/App/ViewModels/MainWindowViewModel.cs`.
- **Evidence:** save-login stores the password under SteamID and account-name
  aliases, but removal passes the SteamID as both identifiers. The account-name
  alias therefore survives removal.
- **Remediation:** delete both original aliases before discarding the account,
  retain a retry marker if the credential service is unavailable, and add
  regression coverage for local removal and successful deactivation.

### 7. Duplicate SteamID imports silently overwrite an authenticator

- **Severity:** Medium
- **Confidence:** High
- **Locations:** the Android add-account and repository import paths, plus
  `src/App/Views/ImportAccountWindow.axaml.cs`, `src/App/Core/Manifest.cs`, and
  `src/SteamAuth/SessionData.cs`.
- **Evidence:** both clients trust the SteamID declared by imported JSON and
  replace the matching manifest entry and `.maFile` without conflict approval,
  identity binding, or a recoverable backup.
- **Remediation:** validate the imported identity, default-deny duplicates or
  require explicit destructive confirmation, write atomically with a backup,
  and bind any authenticated login identity to the imported SteamID.

### 8. Android remains unlocked after backgrounding

- **Severity:** Medium
- **Confidence:** High
- **Locations:** `Application/app/src/main/java/com/sda/mobile/ui/viewmodel/AppViewModel.kt`,
  `Application/app/src/main/java/com/sda/mobile/MainActivity.kt`, and
  `Application/app/src/main/java/com/sda/mobile/ui/nav/SdaNavGraph.kt`.
- **Evidence:** the activity-scoped view model retains the passkey and
  deserialized accounts. A lock method exists but has no caller, and the
  activity has no lifecycle-driven relock.
- **Remediation:** clear all sensitive state and navigation after a short,
  deliberate background grace period and require the passkey or device
  biometric before resuming sensitive destinations.

### 9. Android permits capture of codes and full-secret QR screens

- **Severity:** Medium
- **Confidence:** High
- **Locations:** `Application/app/src/main/java/com/sda/mobile/MainActivity.kt`,
  `Application/app/src/main/java/com/sda/mobile/ui/screens/AccountListScreen.kt`,
  and `Application/app/src/main/java/com/sda/mobile/ui/screens/QrExportScreen.kt`.
- **Evidence:** sensitive screens render current codes and a QR containing the
  complete plaintext account record without `FLAG_SECURE` or equivalent task
  snapshot protection.
- **Remediation:** enable `FLAG_SECURE` before rendering sensitive content,
  obscure task snapshots, retain the export warning, and consider immediate
  reauthentication before QR export.

### 10. Copied Steam Guard codes remain in ordinary clipboards

- **Severity:** Low
- **Confidence:** High
- **Locations:** `src/App/ViewModels/MainWindowViewModel.cs`,
  `src/App/Services/AvaloniaDialogService.cs`, and
  `Application/app/src/main/java/com/sda/mobile/ui/screens/AccountListScreen.kt`.
- **Evidence:** both clients copy current codes without sensitive metadata or a
  compare-and-clear timer.
- **Remediation:** mark Android clips sensitive where supported and
  compare-and-clear on both platforms at the end of the active code period.

### 11. File imports have no size or batch limits

- **Severity:** Low
- **Confidence:** High
- **Locations:** the Android add-account and repository import paths and
  `src/App/Views/ImportAccountWindow.axaml.cs`.
- **Evidence:** both clients buffer an entire selected file before validation;
  desktop also permits multiple files and reads an adjacent manifest.
- **Remediation:** enforce per-file, adjacent-manifest, batch-count, and
  aggregate byte limits before allocation, use bounded streaming, and keep
  Android I/O off the UI dispatcher.

## Coverage and limitations

The review covered all 234 Git-tracked paths at the pinned revision, including
first-party C#, Kotlin, Avalonia XAML, Android resources and configuration,
tests, tracked authenticator artifacts, and release-signing configuration.

- The .NET SDK was unavailable, so a fresh desktop build and a
  restore-resolved transitive NuGet audit did not run.
- JDK 17 and a configured Android SDK were unavailable, so the documented
  Android build, tests, and lint did not run.
- Generated and third-party binaries were inventoried but not decompiled.
- External app-store signing lineage and Steam-side revocation state remain
  outside repository evidence.

## Recommended implementation order

1. Complete credential and signing-key incident response.
2. Remove and purge sensitive artifacts; add preventive CI controls.
3. Fix duplicate-import replacement and credential-store cleanup.
4. Add Android lifecycle relock and screen-capture protections.
5. Introduce a versioned AEAD/KDF format with cross-client migration tests.
6. Harden filesystem permissions, clipboard handling, and import bounds.
