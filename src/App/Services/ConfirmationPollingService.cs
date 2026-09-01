using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace SteamDesktopAuthenticator.Services
{
    /// <summary>
    /// Polls FetchConfirmationsAsync for every enabled account on an interval and reports back
    /// the merged set. One failing/expired account (e.g. session needs re-login) does not block
    /// confirmations from other accounts from showing - each account is fetched independently.
    ///
    /// On a failure that looks like an expired session, this now asks the shared
    /// <see cref="SessionRecoveryService"/> to refresh/re-authenticate the account (Task 2) and,
    /// if that succeeds, retries the fetch once for that account before giving up - all within
    /// the same poll cycle, so a routine token refresh never even surfaces as a visible failure.
    /// </summary>
    public class ConfirmationPollingService : IDisposable
    {
        public event Action<List<(AccountViewModel Owner, Confirmation Confirmation)>>? ConfirmationsUpdated;
        public event Action<AccountViewModel, Exception>? AccountPollFailed;

        private readonly Func<IEnumerable<AccountViewModel>> _accountsProvider;
        private readonly SessionRecoveryService _recoveryService;
        private readonly Func<SteamGuardAccount, bool> _persistAccount;
        private Timer? _timer;
        private int _isPolling; // 0/1 guard against overlapping poll cycles

        public ConfirmationPollingService(
            Func<IEnumerable<AccountViewModel>> accountsProvider,
            SessionRecoveryService recoveryService,
            Func<SteamGuardAccount, bool> persistAccount)
        {
            _accountsProvider = accountsProvider;
            _recoveryService = recoveryService;
            _persistAccount = persistAccount;
        }

        public void Start(TimeSpan interval)
        {
            Stop();
            _timer = new Timer(async _ => await PollOnceAsync(), null, TimeSpan.Zero, interval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public async Task PollOnceAsync()
        {
            if (Interlocked.Exchange(ref _isPolling, 1) == 1) return; // skip overlapping cycles
            try
            {
                var accounts = _accountsProvider().Where(a => a.Enabled).ToList();
                var results = new List<(AccountViewModel, Confirmation)>();

                var tasks = accounts.Select(async acc =>
                {
                    try
                    {
                        Confirmation[]? confs;
                        try
                        {
                            confs = await acc.Account.FetchConfirmationsAsync();
                        }
                        catch (Exception fetchEx) when (LooksLikeExpiredSession(fetchEx) || acc.Account.Session.IsAccessTokenExpired())
                        {
                            Logger.Info("Confirmations", $"{Logger.AccountRef(acc.Account.Session.SteamID)} fetch failed ({fetchEx.Message}) - attempting session recovery.");
                            var outcome = await _recoveryService.EnsureValidSessionAsync(
                                acc.Account, acc.Meta.SaveLoginEnabled, () => _persistAccount(acc.Account));

                            if (outcome != SessionRecoveryService.RecoveryOutcome.Recovered)
                            {
                                throw; // rethrow original exception - handled by the outer catch below
                            }

                            // Retry the original request now that the session should be valid
                            // again (Task 2: "retry the original request").
                            confs = await acc.Account.FetchConfirmationsAsync();
                        }

                        int count = confs?.Length ?? 0;
                        Dispatcher.UIThread.Post(() => acc.PendingConfirmationCount = count);
                        if (confs != null)
                        {
                            lock (results)
                            {
                                foreach (var c in confs) results.Add((acc, c));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.Post(() => acc.PendingConfirmationCount = 0);
                        Dispatcher.UIThread.Post(() => AccountPollFailed?.Invoke(acc, ex));
                    }
                });

                await Task.WhenAll(tasks);
                Dispatcher.UIThread.Post(() => ConfirmationsUpdated?.Invoke(results));
            }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        /// <summary>Detects an expired/invalid session from a failed request. Two distinct
        /// failure shapes have to be covered here - this used to only catch the first one, which
        /// was the actual bug behind automatic re-login never firing:
        ///
        ///  1. Steam accepts the HTTP request but replies 200 OK with a "the app-level session is
        ///     stale" JSON body (SteamAuth's FetchConfirmationInternal throws a plain Exception
        ///     for this - message text is the only signal without changing SteamAuth's public
        ///     exception shape, so we match those known phrases).
        ///  2. The access token itself is dead, so Steam rejects the request at the HTTP layer
        ///     (401/403) before any JSON is returned. WebClient surfaces this as a WebException
        ///     whose Message is just the generic HTTP status text (e.g. "The remote server
        ///     returned an error: (401) Unauthorized."), which never matched the phrases above -
        ///     so every real "your access token is expired" failure fell through to the generic
        ///     catch below and reported as a plain poll failure, without ever calling
        ///     SessionRecoveryService. This is the missing "Task 2" connection: the caller here
        ///     now also checks the WebException's actual status code, and (see the call site)
        ///     the account's own local token-expiry check as a further backstop, so any of the
        ///     three signals is enough to trigger automatic recovery.</summary>
        private static bool LooksLikeExpiredSession(Exception ex)
        {
            if (ex.Message != null &&
                (ex.Message.Contains("Needs Authentication", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("Invalid Access Token", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("access_token", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (ex is System.Net.WebException webEx &&
                webEx.Response is System.Net.HttpWebResponse httpResponse &&
                (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                 httpResponse.StatusCode == System.Net.HttpStatusCode.Forbidden))
            {
                return true;
            }

            return false;
        }

        public void Dispose() => Stop();
    }
}
