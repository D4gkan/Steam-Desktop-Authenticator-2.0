using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Core.Security;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Services
{
    /// <summary>
    /// Centralized automatic session-recovery mechanism (Task 2).
    ///
    ///   caller detects a failure
    ///        -> EnsureValidSessionAsync
    ///             -> access token expired only? refresh it (cheap, no password needed)
    ///             -> refresh token itself dead/invalid? saved password? no -> give up, caller
    ///                must ask the user to log in
    ///                                              yes -> headless credentials re-login using
    ///                the saved password + this account's own Steam Guard code -> new session
    ///                -> persist it -> caller retries the original request
    ///
    /// Loop prevention: each account gets a failure counter. After
    /// <see cref="MaxConsecutiveFailures"/> consecutive failed automatic re-login attempts, the
    /// account is backed off for <see cref="CoolDownPeriod"/> before automatic re-login is tried
    /// again, so a bad/changed saved password cannot spin forever
    /// (request -> expired -> login -> failure -> request -> expired -> login -> ...).
    ///
    /// Concurrency: a per-account SemaphoreSlim ensures that if several requests discover an
    /// expired session for the same account at the same time (e.g. a confirmation poll and a
    /// confirm/reject click racing each other), only one of them actually performs the
    /// re-login; the others wait for it and reuse its result instead of each starting their own
    /// login session.
    /// </summary>
    public class SessionRecoveryService
    {
        private const int MaxConsecutiveFailures = 3;
        private static readonly TimeSpan CoolDownPeriod = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();
        private readonly ConcurrentDictionary<ulong, FailureState> _failures = new();

        private class FailureState
        {
            public int ConsecutiveFailures;
            public DateTime CoolDownUntil = DateTime.MinValue;
        }

        public enum RecoveryOutcome
        {
            /// <summary>Session was already valid, or was refreshed/re-authenticated successfully.
            /// The caller should retry its original request.</summary>
            Recovered,
            /// <summary>Session is invalid and automatic recovery is not possible or not enabled
            /// for this account (no saved password). The caller must ask the user to log in.</summary>
            RequiresManualLogin,
            /// <summary>Automatic recovery was attempted but failed (bad saved password, network
            /// error, Steam Guard/email code required, etc.), or the account is currently in its
            /// cool-down window after repeated failures. The caller should surface this passively
            /// rather than retrying immediately, to avoid an authentication loop.</summary>
            Failed,
        }

        /// <summary>
        /// Ensures <paramref name="account"/> has a usable session, refreshing the access token
        /// or performing a full automatic re-login as needed. <paramref name="persistAccount"/>
        /// is called (by the ui layer, which alone knows the current manifest/passkey) after a
        /// successful refresh or re-login so the new tokens are saved to the .maFile.
        /// </summary>
        public async Task<RecoveryOutcome> EnsureValidSessionAsync(SteamGuardAccount account, bool saveLoginEnabled, Func<bool> persistAccount)
        {
            ulong steamId = account.Session.SteamID;

            if (!account.Session.IsAccessTokenExpired())
            {
                return RecoveryOutcome.Recovered;
            }

            var gate = _gates.GetOrAdd(steamId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another caller may have already fixed this while we were waiting for the gate.
                if (!account.Session.IsAccessTokenExpired())
                {
                    return RecoveryOutcome.Recovered;
                }

                var state = _failures.GetOrAdd(steamId, _ => new FailureState());
                if (state.ConsecutiveFailures >= MaxConsecutiveFailures && DateTime.UtcNow < state.CoolDownUntil)
                {
                    Logger.Warn("Auth", $"{Logger.AccountRef(steamId)} skipping automatic re-login - in cool-down after {state.ConsecutiveFailures} consecutive failures until {state.CoolDownUntil:O}.");
                    return RecoveryOutcome.Failed;
                }

                Logger.Info("Auth", $"{Logger.AccountRef(steamId)} session expired - attempting recovery.");

                // Step 1: cheap path. If the refresh token is still valid, just mint a new access
                // token - no password needed, nothing to log in to.
                if (!account.Session.IsRefreshTokenExpired())
                {
                    try
                    {
                        await account.Session.RefreshAccessToken().ConfigureAwait(false);
                        persistAccount();
                        state.ConsecutiveFailures = 0;
                        Logger.Info("Auth", $"{Logger.AccountRef(steamId)} session refreshed via refresh token.");
                        return RecoveryOutcome.Recovered;
                    }
                    catch (Exception ex)
                    {
                        // Fall through to full re-login below - the refresh token may have just
                        // been revoked server-side even though our local expiry check didn't
                        // catch it yet.
                        Logger.Warn("Auth", $"{Logger.AccountRef(steamId)} refresh-token renewal failed: {ex.Message}. Falling back to full re-login if a saved password is available.");
                    }
                }

                // Step 2: full re-login. Only possible if the person opted in to saving their
                // password (Task 1 - off by default, never attempted otherwise).
                if (!saveLoginEnabled)
                {
                    Logger.Info("Auth", $"{Logger.AccountRef(steamId)} no saved password - manual login required.");
                    return RecoveryOutcome.RequiresManualLogin;
                }

                string? password;
                try
                {
                    var store = CredentialStoreFactory.Get();
                    password = CredentialStoreCompat.TryGetPassword(store, steamId, account.AccountName);
                }
                catch (CredentialStoreException ex)
                {
                    Logger.Error("Auth", $"{Logger.AccountRef(steamId)} could not read saved password from {CredentialStoreFactory.Get().DisplayName}: {ex.Message}");
                    return RecoveryOutcome.RequiresManualLogin;
                }

                if (string.IsNullOrEmpty(password))
                {
                    Logger.Warn("Auth", $"{Logger.AccountRef(steamId)} save-login is enabled but no password is stored - manual login required.");
                    return RecoveryOutcome.RequiresManualLogin;
                }

                Logger.Info("Auth", $"{Logger.AccountRef(steamId)} automatic re-login started.");
                try
                {
                    var newSession = await PerformHeadlessLoginAsync(account, password).ConfigureAwait(false);
                    account.Session = newSession;
                    account.FullyEnrolled = true;
                    persistAccount();
                    state.ConsecutiveFailures = 0;
                    Logger.Info("Auth", $"{Logger.AccountRef(steamId)} automatic re-login succeeded; session refreshed and original request can be retried.");
                    return RecoveryOutcome.Recovered;
                }
                catch (Exception ex)
                {
                    state.ConsecutiveFailures++;
                    if (state.ConsecutiveFailures >= MaxConsecutiveFailures)
                    {
                        state.CoolDownUntil = DateTime.UtcNow.Add(CoolDownPeriod);
                    }
                    Logger.Error("Auth", $"{Logger.AccountRef(steamId)} automatic re-login failed ({state.ConsecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}");
                    return RecoveryOutcome.Failed;
                }
                finally
                {
                    // Never keep the plaintext password around longer than this method call.
                    password = null;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Resets the failure/cool-down counter for an account - called after a
        /// successful *manual* login, so a fixed password immediately gets a fresh chance at
        /// automatic recovery instead of waiting out a stale cool-down.</summary>
        public void ResetFailureState(ulong steamId) => _failures.TryRemove(steamId, out _);

        private static async Task<SessionData> PerformHeadlessLoginAsync(SteamGuardAccount account, string password)
        {
            var steamClient = new SteamClient();
            steamClient.Connect();

            int waited = 0;
            while (!steamClient.IsConnected && waited < 15000)
            {
                await Task.Delay(500).ConfigureAwait(false);
                waited += 500;
            }
            if (!steamClient.IsConnected)
            {
                throw new Exception("Could not connect to Steam.");
            }

            var authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = account.AccountName,
                Password = password,
                IsPersistentSession = false,
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                ClientOSType = EOSType.Android9,
                // Headless authenticator: this account already has SDA as its Steam Guard
                // authenticator, so device codes are generated locally from the shared secret
                // with no UI interaction. If Steam additionally demands an emailed code (e.g.
                // unrecognized IP) this will fail rather than pop a dialog - that is a real
                // Steam-side requirement automatic recovery cannot bypass, and is reported back
                // to the caller as a failed recovery attempt rather than silently hanging.
                Authenticator = new HeadlessAuthenticator(account),
            }).ConfigureAwait(false);

            var pollResponse = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);

            return new SessionData
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = pollResponse.AccessToken,
                RefreshToken = pollResponse.RefreshToken,
            };
        }

        /// <summary>IAuthenticator implementation for unattended automatic re-login: answers
        /// device-code prompts locally (this account's own shared_secret), and fails fast on
        /// anything that would otherwise require a person at the keyboard (email codes, device
        /// confirmation on another already-approved device) rather than blocking indefinitely.</summary>
        private class HeadlessAuthenticator : IAuthenticator
        {
            private readonly SteamGuardAccount _account;
            public HeadlessAuthenticator(SteamGuardAccount account) => _account = account;

            public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);

            public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
            {
                if (previousCodeWasIncorrect)
                {
                    throw new Exception("Steam rejected the device code generated from the saved authenticator secret.");
                }
                return await _account.GenerateSteamGuardCodeAsync().ConfigureAwait(false);
            }

            public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
            {
                throw new Exception("Steam requires an emailed Steam Guard code for this login attempt, which cannot be completed automatically.");
            }
        }
    }
}
