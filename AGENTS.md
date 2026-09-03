# Repository Guidelines

## Project Structure & Module Organization

The repository contains two clients that share Steam authenticator file formats. `src/App/` is the .NET 8 Avalonia desktop UI, organized into `Views/`, `ViewModels/`, `Services/`, `Core/`, and `Assets/`. `src/SteamAuth/` contains reusable Steam authentication and confirmation logic. `Application/` is the Android companion app; production Kotlin lives under `Application/app/src/main/java/com/sda/mobile/`, resources under `src/main/res/`, and unit tests under `src/test/`. Keep `.maFile` and `manifest.json` serialization compatible across both clients.

## Build, Test, and Development Commands

Run desktop commands from the repository root:

- `dotnet restore` — restore solution packages.
- `dotnet build SteamDesktopAuthenticator.sln -c Release` — compile both desktop projects.
- `dotnet run --project src/App/App.csproj` — launch the desktop app locally.

Run Android commands from `Application/` with JDK 17 and an Android SDK installed:

- `./gradlew assembleDebug` — build the debug APK.
- `./gradlew test` — run JVM unit tests.
- `./gradlew lint` — run Android static analysis.

## Coding Style & Naming Conventions

Follow the surrounding code. C# uses four-space indentation, PascalCase for types and public members, `_camelCase` for private fields, nullable reference types, and async method names ending in `Async`. Keep Avalonia markup in paired `.axaml` and `.axaml.cs` files. Kotlin uses four spaces, PascalCase types, camelCase functions/properties, and package-by-feature folders such as `data`, `network`, and `ui`. Prefer focused services and repositories over adding workflow logic to views.

## Testing Guidelines

Android tests use JUnit 4 and files named `*Test.kt`; add tests beside the matching package under `Application/app/src/test/`. Run `./gradlew test` before submitting Android changes. The desktop solution currently has no test project, so at minimum build the solution and manually exercise affected UI/account flows. Never test authenticator linking against a primary Steam account; use a disposable account.

## Commit & Pull Request Guidelines

Older history uses brief messages such as `Fixed Authentication` and release tags like `v2.0.3`. New commits must use imperative Conventional Commits, for example `fix(auth): refresh expired sessions`, with one coherent change per commit. Pull requests should explain behavior and risk, link the relevant issue or ADR, list verification commands, and include screenshots for UI changes. Call out schema, security, privacy, signing, or deployment effects explicitly.

## Security & Configuration

Treat `.maFile` data, revocation codes, passwords, QR exports, and signing material as secrets. Do not commit generated account data or new credentials. Coordinate any release-signing configuration changes with a maintainer.
