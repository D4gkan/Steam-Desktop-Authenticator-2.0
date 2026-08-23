<div align="center">

<img src="./icon.png" height="110" width="110" alt="Steam Desktop Authenticator icon" />

# Steam Desktop Authenticator

**A desktop implementation of Steam's mobile authenticator app.**

<sub><b>We are not affiliated with Steam or Scrap.TF in any way!</b> This project is run by community volunteers.</sub>

[![Latest Release](https://img.shields.io/badge/version-2.0.0-3fb950?style=for-the-badge)](#-download--install)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20·%20Linux%20(beta)-0078D6?style=for-the-badge&logo=linux)](#-download--install)
[![License](https://img.shields.io/badge/license-MIT-blue?style=for-the-badge)](LICENSE)

</div>

---

> ### ⚠️ Read this before you do anything else
> - **This project is not affiliated with, sponsored by, or endorsed by Valve Corporation or Steam.** It is an independent, community-run tool.
> - **This goes against the entire point of two-factor authentication.** The official Steam Mobile app keeps your authenticator on a *separate device* from your PC on purpose — that separation is what makes 2FA effective. Running the authenticator on the same computer you use Steam on defeats that protection: if your PC is ever compromised, whatever compromises it can also control your Steam Guard codes and trade confirmations.
> - SDA does **not** protect your account; it only lets you use Steam Guard-gated features without a phone.
> - **Use this software entirely at your own risk.** If you own a phone that can run the official Steam Mobile app, use that instead.
> - Fake copies of SDA that steal Steam accounts have circulated online. **Only ever download SDA from this repository's official [Releases page](https://github.com/D4gkan/Steam-Desktop-Authenticator-2.0/releases).**

---

## Table of Contents

- [What is this?](#-what-is-this)
- [Feature overview](#-feature-overview)
- [Download & Install](#-download--install)
- [First-time setup](#-first-time-setup)
- [Using the app](#-using-the-app)
  - [The account list](#the-account-list)
  - [Getting a login code](#getting-a-login-code)
  - [Trade & market confirmations](#trade--market-confirmations)
  - [Adding another account](#adding-another-account)
  - [Importing existing maFiles](#importing-existing-mafiles)
  - [Encryption](#encryption)
  - [Settings](#settings)
  - [Removing / deactivating an authenticator](#removing--deactivating-an-authenticator)
- [Command line options](#-command-line-options)
- [Backups & disaster recovery](#-backups--disaster-recovery)
- [Troubleshooting](#-troubleshooting)
- [FAQ](#-faq)
- [Credits](#-credits)
- [Contributing & support](#-contributing--support)

---

## 🧭 What is this?

Steam Desktop Authenticator recreates the core functionality of the official Steam Mobile app's authenticator on your PC:

- Generates the same rotating **Steam Guard login codes** the mobile app produces.
- Lets you **approve or deny trade offers and market listings** that require mobile confirmation.
- Manages **multiple Steam accounts** side by side, each with its own authenticator file (`maFile`).
- Optionally **encrypts** those files with a password so a stolen copy of your `maFiles` folder isn't immediately usable.

It's aimed at people who manage several Steam accounts (traders, bot operators, community volunteers) and need authenticator access without juggling multiple phones.

---

## ✨ Feature overview

| Feature | Description |
|---|---|
| 🔑 **Live login codes** | Generates a fresh 5-character Steam Guard code for the selected account, with a visual countdown until it refreshes. |
| 👥 **Multi-account management** | Add, import, rename, reorder (move up/down), and remove as many linked accounts as you want from one window. |
| 🔍 **Search & filter** | Instantly filter your account list by name as you type. |
| ✅ **Trade confirmations** | View all pending trade offers and market listings across your accounts, with details on what's being confirmed. |
| ⚡ **Confirm / Reject, individually or in bulk** | Approve or deny a single confirmation, or use **Confirm All** / **Reject All** to clear the whole queue at once. |
| 🤖 **Auto-confirm rules** | Optionally auto-confirm market transactions and/or trades automatically, without manual approval. |
| ⏱️ **Background polling** | Periodically checks for new confirmations in the background so the list stays current — configurable to check only the selected account or all of them. |
| 🔒 **Optional file encryption** | Protects your local `maFiles` with a password/passkey so they can't be used as-is if someone copies them off your machine. |
| ➕ **Add new authenticator** | Log in to Steam directly from the app and link a brand-new mobile authenticator to an account. |
| 📥 **Import existing maFile(s)** | Bring in authenticator files you already have (e.g. from another install or backup) via a simple file picker — single or batch import supported. |
| 📇 **Session refresh & re-login** | Force-refresh a stale Steam session or fully re-authenticate an account without losing its linked authenticator. |
| ❌ **Deactivate authenticator** | Safely remove Steam Guard from an account (with a confirmation-code sanity check) directly from the app, mirroring Steam's own safety flow. |
| 🖱️ **Quick-copy codes** | One click copies the current login code to your clipboard. |
| 🧩 **Per-account enable/disable** | Temporarily disable an account's authenticator polling without deleting it. |
| 🎨 **Modern UI** | Clean, dark, resizable interface with a split view — account list on the left, confirmations on the right. |

---

## 🐧 Platform support

This build is written on **Avalonia UI**, a cross-platform .NET framework, rather than the original WinForms codebase — so unlike classic SDA, it isn't tied to Windows.

- **Windows 10+** — fully supported.
- **Linux x64** — a build is published in the release, but functional testing hasn't been confirmed yet. If you try it, please open an issue and let us know whether it launches and works correctly on your distro so this can be marked as verified.

To build it yourself on Linux:

```
dotnet restore
dotnet build -c Release
dotnet run --project src/App/App.csproj
```

For a distributable, self-contained Linux build:

```
dotnet publish src/App/App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
```

> **Note:** if you install the .NET SDK on Linux, use [Microsoft's official install script](https://dot.net/v1/dotnet-install.sh) rather than your distro's package manager where possible — some distro packages (including on Mint/Ubuntu-based systems) ship incomplete `dotnet` builds that fail with a `libhostfxr.so not found` error.

## 📦 Download & Install

### 🪟 Windows

<table>
<tr><td>1️⃣</td><td>Download <a href="https://github.com/D4gkan/Steam-Desktop-Authenticator-2.0/releases/download/v2.0.0/SteamDesktopAuthenticator-v2.0.0-win-x64.zip"><code>SteamDesktopAuthenticator-v2.0.0-win-x64.zip</code></a>.</td></tr>
<tr><td>2️⃣</td><td>Right-click the downloaded zip and choose <b>Extract All…</b> (or use 7-Zip/WinRAR) to a <b>safe, permanent folder</b> — e.g. <code>C:\SDA</code>. Don't run it from inside your Downloads or a temp folder, since losing this folder later can mean losing access to your Steam account(s).</td></tr>
<tr><td>3️⃣</td><td>Open the extracted folder and double-click <b>SteamDesktopAuthenticator.exe</b> to launch the app.</td></tr>
<tr><td>4️⃣</td><td>If Windows SmartScreen shows a warning (common for apps without a paid code-signing certificate), click <b>More info → Run anyway</b>.</td></tr>
</table>

No separate .NET install is required — the Windows build is self-contained.

### 🐧 Linux

<table>
<tr><td>1️⃣</td><td>Download <a href="https://github.com/D4gkan/Steam-Desktop-Authenticator-2.0/releases/download/v2.0.0/SteamDesktopAuthenticator-v2.0.0-linux-x64.zip"><code>SteamDesktopAuthenticator-v2.0.0-linux-x64.zip</code></a>.</td></tr>
<tr><td>2️⃣</td><td>Extract it to a safe, permanent folder. From a terminal:<br><pre>unzip SteamDesktopAuthenticator-v2.0.0-linux-x64.zip -d ~/SDA
cd ~/SDA</pre></td></tr>
<tr><td>3️⃣</td><td>Make the binary executable (only needs to be done once):<br><pre>chmod +x SteamDesktopAuthenticator</pre></td></tr>
<tr><td>4️⃣</td><td>Launch it:<br><pre>./SteamDesktopAuthenticator</pre>Or double-click it from your file manager if it's set to allow executing files.</td></tr>
</table>

> **Linux status:** this build is published but hasn't been fully verified across distros yet — see [Platform support](#-platform-support) above. If it doesn't launch, check the [Troubleshooting](#-troubleshooting) section below.

### Both platforms

Wherever you extract the app, make sure it's somewhere you'll **remember and back up long-term** — specifically the `maFiles` folder the app creates there, since that's what holds your linked authenticators. Losing it (without a saved revocation code) can mean permanently losing access to your Steam account.

**Clicking "Download ZIP" on the repository's code page will not give you a working build if this project uses git submodules — always use the [official Releases page](https://github.com/D4gkan/Steam-Desktop-Authenticator-2.0/releases/latest) zips linked above.**

---

## 🚀 First-time setup

<table>
<tr><td>1️⃣</td><td>Open Steam Desktop Authenticator and choose to set up a new account.</td></tr>
<tr><td>2️⃣</td><td>Log in with your Steam username and password. <b>You still need a phone that can receive SMS</b> for the initial linking step — Steam requires this to add a new authenticator.</td></tr>
<tr><td>3️⃣</td><td>Enter the SMS code Steam sends you to confirm the phone number.</td></tr>
<tr><td>4️⃣</td><td>SDA generates and links a new mobile authenticator to your account, the same as the official app would.</td></tr>
<tr><td>5️⃣</td><td><b>Write down your revocation code</b> when it's shown. This is your emergency key if you ever lose your files — store it somewhere safe and offline.</td></tr>
<tr><td>6️⃣</td><td>You'll be asked whether to set up encryption for your local files. This is optional but <b>highly recommended</b>.</td></tr>
<tr><td>7️⃣</td><td>Get your <b>Steam Guard backup codes</b> at <a href="https://store.steampowered.com/twofactor/manage">store.steampowered.com/twofactor/manage</a> → "Get Backup Codes" — print or save them somewhere safe.</td></tr>
</table>

---

## 🖥️ Using the app

### The account list

The left panel lists every account you've added. Each entry shows:
- The account's display name and initial avatar.
- The current Steam Guard code, with a progress bar showing time until the next refresh.
- A **disabled** tag if you've turned off polling for that account.
- A badge with the number of pending confirmations, if any.

Click an account to select it. Use the search box at the top to filter the list by name. You can reorder accounts by moving them up or down, and rename or remove an account from its context menu (⋮).

### Getting a login code

Select an account to see its live Steam Guard code in the list. Click the copy icon next to the code to copy it straight to your clipboard for pasting into the Steam login screen.

### Trade & market confirmations

The right panel shows all **pending confirmations** — trades and market listings waiting on mobile approval — pulled from your accounts.

- Click **Refresh** to manually re-check for new confirmations.
- Approve or deny a single item directly on its card.
- Use **Confirm All** or **Reject All** to process every pending confirmation at once.
- Enable **"Periodically check for confirmations"** in Settings so this list updates automatically in the background — either for the selected account only, or across all accounts.

### Adding another account

Click **＋ Add Account** in the top bar to either:
- **Log in & link a new authenticator** — walks you through Steam login and mobile authenticator setup for an account that doesn't have one yet, or
- **Import an existing `maFile`** — choose one or more `.maFile` files from disk to add accounts you've already linked elsewhere.

### Importing existing maFiles

If you're moving from another computer or restoring from backup, use **Choose .maFile(s)…** in the Add Account window. You can select multiple files at once to import several accounts in one go.

### Encryption

From the top bar you can:
- **🔒 Setup Encryption** — protect your `maFiles` with a password/passkey. You'll need to enter it whenever the app starts.
- **🔓 Remove Encryption** — decrypt your files back to plain storage.

While locked, the app shows a lock screen until you enter your passkey — your account and confirmation data stays hidden until then.

### Settings

Open **⚙ Settings** to configure:

| Option | What it does |
|---|---|
| Periodically check for confirmations | Turns on automatic background polling. |
| Check all accounts, not just the selected one | Expands polling to every account instead of just the one you're viewing. |
| Auto-confirm market transactions | Automatically approves market-related confirmations as they arrive. |
| Auto-confirm trades | Automatically approves trade confirmations as they arrive. |

> ⚠️ Auto-confirming trades or market listings means SDA will approve them **without you reviewing each one**. Only enable this if you fully trust the environment SDA is running in.

### Removing / deactivating an authenticator

From an account's context menu, choose **Deactivate Authenticator** to remove Steam Guard from that account. The app will:
1. Refresh your session if needed to make sure it's still valid.
2. Ask whether to remove Steam Guard entirely or fall back to email-based codes.
3. Require you to enter a currently-generated code as a safety check, confirming this really is a working copy of the authenticator.
4. Call Steam's official removal process and delete the local `maFile` once it succeeds.

This mirrors the safety flow of Steam's own authenticator removal, so you can't accidentally lock yourself out.

---

## ⌨️ Command line options

```
-k [encryption key]
  Set your encryption key when opened
-s
  Auto-minimize to tray when opened
```

---

## 💾 Backups & disaster recovery

- **Always back up your entire `maFiles` folder.** If you lose it — or your encryption key — and didn't save your revocation code, there is no way to recover the account.
- If you lost your `maFiles` **or** your encryption key, go to [Steam's Manage Two-Factor page](https://store.steampowered.com/twofactor/manage), click **"Remove Authenticator,"** and enter the revocation code you saved during setup.
- Didn't write down your revocation code either? Your only remaining option is to contact [Steam Support](https://support.steampowered.com/) and explain that you've lost both your mobile authenticator and revocation code.

---

## 🛠️ Troubleshooting

**Trade confirmation list is blank or just white**
1. Open the **Selected Account** menu and click **Force session refresh**.
2. If that doesn't help, open the **Selected Account** menu again and click **Login again**, then sign back in to your Steam account.

If your issue isn't listed here or nothing above fixes it, please open an issue on the issue tracker. When sharing logs, upload them to a paste service like [Pastebin](http://www.pastebin.com) rather than pasting large blocks directly into the issue.

---

## ❓ FAQ

<details>
<summary><b>Is this safe to use?</b></summary>
<br>
It's as safe as the computer it runs on. Because SDA stores your authenticator secret on your PC, anyone with access to your machine (physically or via malware) can potentially use it to approve trades or generate login codes. If security is your top priority, use the official Steam Mobile app on a phone instead.
</details>

<details>
<summary><b>Do I need a phone at all?</b></summary>
<br>
Yes, at least once. Steam requires SMS verification of a phone number when you first link a mobile authenticator to an account, even if all future logins happen through SDA.
</details>

<details>
<summary><b>What happens if I forget my encryption passkey?</b></summary>
<br>
You won't be able to decrypt your local <code>maFiles</code>. Your only path back into the account is the revocation code you saved during setup, via Steam's Manage Two-Factor page.
</details>

<details>
<summary><b>Can I use SDA on more than one computer?</b></summary>
<br>
Yes, by copying your <code>maFiles</code> folder between machines (or importing individual <code>.maFile</code>s). Keep in mind that whoever holds a copy of an unencrypted <code>maFile</code> can generate codes and approve confirmations for that account, so treat every copy as sensitive.
</details>

---

## 🙌 Credits

Special thanks to **Jessecar96** ([@jessecar96](https://github.com/jessecar96)), the original creator of [Steam Desktop Authenticator](https://github.com/jessecar96/steamdesktopauthenticator), and all contributors to the original project.

---

## 🤝 Contributing & support

This project is maintained by volunteers in their spare time — **no formal support is provided**. Bug reports and pull requests are welcome via the issue tracker. Please read the warnings at the top of this document before asking for help recovering an account; in most cases the revocation code is the only way back in.
