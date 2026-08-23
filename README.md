# Steam Desktop Authenticator — Redesigned

A cross-platform (Windows/Linux/macOS) rebuild of Steam Desktop Authenticator on
**Avalonia UI** (.NET 8), with a single main dashboard (account list + codes on the left,
a persistent all-accounts confirmations panel on the right), a dark minimal theme, and the
original security-critical logic ported as faithfully as possible rather than rewritten from
scratch.

## ⚠️ Read this first

**This has not been compiled or run.** The sandbox this was built in only has network access to
`github.com`/`nuget`-adjacent source mirrors, not the actual NuGet package feed or a .NET SDK, so
there was no way to `dotnet build` and fix compiler errors here. I verified every non-trivial API
call (SteamKit2's auth session flow, Avalonia's storage provider / visual tree / clipboard APIs,
etc.) directly against each library's real source on GitHub rather than from memory, which
eliminates the most common source of errors — but it does **not** substitute for actually
compiling. Please treat this as a strong first draft:

```bash
cd SteamDesktopAuthenticator
dotnet restore
dotnet build
```

Expect to fix a handful of small issues (missing usings, a typo, an XAML resource key mismatch).
None of it should require re-architecting anything.

## What was ported vs. rewritten

**Ported near-verbatim** (same file formats, same decisions, same edge cases):
- `Manifest.cs` — same `manifest.json` schema, same `.maFile` paths, same encryption logic. The
  only change: passkey prompts go through an `IPasskeyPrompter` interface instead of directly
  constructing WinForms dialogs, so the same class works with any UI. **Existing `manifest.json` /
  `.maFile` files from stock SDA will load in this build, and vice versa.**
- `FileEncryptor.cs` — identical AES-256-CBC/PBKDF2(SHA1, 50k iterations) parameters, just using
  `Aes.Create()` instead of the Windows-only `RijndaelManaged` (byte-identical algorithm, just
  cross-platform).
- `LoginWindow` — the entire SteamKit2 credential-login → `AuthenticatorLinker.AddAuthenticator`
  → phone number → email confirmation → SMS finalize → revocation-code-confirmation flow is
  ported step-by-step from `LoginForm.cs`, including all the original's error messages.
- `ImportAccountWindow` — same `.maFile` + adjacent `manifest.json` salt/IV lookup logic as
  `ImportAccountForm.cs`, including the "current manifest must be unencrypted to import" rule.
- `SettingsWindow` — same fields, same "this reduces your security, continue?" warning gate on
  the two auto-confirm checkboxes.
- The `SteamAuth` library itself (session/TOTP/confirmation logic) — pulled directly from the
  upstream submodule your zip was missing (`github.com/geel9/SteamAuth`), untouched.

**New, on top of the ported logic:**
- `UiMetaStore` / `AccountMeta` — custom display names, drag-drop order, and enabled/disabled
  state, stored in a **separate `maFiles/ui-meta.json` sidecar file**. This was a deliberate
  choice: it means `manifest.json` and every `.maFile` stay byte-for-byte what stock SDA would
  produce, so you can point stock SDA at the same `maFiles` folder and it'll work, ignoring the
  sidecar file entirely.
- The dashboard itself: rotating-code cards with a progress ring, a permanent right-side
  confirmations panel merging every enabled account's pending confirmations, Confirm All/Reject
  All, and manual drag-to-reorder (implemented by hand via pointer events, since Avalonia has no
  built-in list drag-reorder).

## Known gaps / things to verify by hand

I want to be specific about what's *not* done rather than let it hide in a big diff:

1. **Not compiled.** See above — top priority before anything else.
2. **`CaptchaForm` / old-style login captcha is not ported.** Modern SteamKit2 credential auth
   (what `LoginForm.cs` already used) doesn't hit Steam's old captcha flow in normal cases, so
   this is likely dead code in the original too, but I haven't verified that assumption against
   a real login attempt.
3. **`TradePopupForm`** (the original's popup notification for a single new trade confirmation)
   was intentionally **not** ported — your spec explicitly says no toast/popup notification
   systems, and the dashboard's confirmations panel replaces its purpose.
4. **Auto-confirm settings** (`AutoConfirmMarketTransactions` / `AutoConfirmTrades`) are read from
   and saved to `manifest.json` by `SettingsWindow` for compatibility, but I did not find (and so
   did not port) an actual background auto-accept loop in the original's `MainForm.cs` — I only
   partially reviewed that file given time constraints. If stock SDA does silently auto-accept
   confirmations matching these flags, that loop still needs to be added here; right now the
   checkboxes are inert.
5. **`periodic_checking` checkbox**: in the original, this gated whether *any* background
   confirmation checking happened. Your spec requires the dashboard's confirmation panel to
   always be live, so I kept polling always-on and only use the checkbox's *interval* value.
   Worth confirming this matches what you want.
6. **Drag-and-drop reorder** is implemented via raw pointer events (press/move/release + hit
   testing), not a library — it's a common, well-understood pattern but genuinely needs to be
   clicked around in a running build to confirm it feels right; I could not eyeball this one.
7. **20-account load test, multi-account simultaneous confirmations, network interruption/Steam
   outage handling, restart persistence** — all explicitly called out in your spec as things to
   test. I could not run the app, so none of these have been exercised even once. Please treat
   the whole app as unverified until you've run through this list.

## Project layout

```
SteamDesktopAuthenticator/
  SteamDesktopAuthenticator.sln
  src/
    SteamAuth/              # ported from github.com/geel9/SteamAuth, untouched
    App/
      Core/                 # Manifest, FileEncryptor, UiMetaStore, IPasskeyPrompter, AvaloniaAuthenticator
      Services/              # IDialogService/AvaloniaDialogService, ConfirmationPollingService
      ViewModels/            # MainWindowViewModel, AccountViewModel, ConfirmationViewModel
      Views/                 # MainWindow (dashboard), LoginWindow, ImportAccountWindow,
                              # SettingsWindow, PhoneInputWindow, PromptDialog
      Styles/Theme.axaml     # dark theme, amber accent, rounded corners
```

## Design notes

- **Accent color**: warm amber (`#F0A63A`), chosen to read as its own product rather than
  echoing Steam's blue, per your "designer picks the accent" instruction.
- **No theme switcher** was added, per your "users should not be able to change the
  background/theme" requirement.
- **No toast/sound notifications** were added anywhere, per your requirement.
