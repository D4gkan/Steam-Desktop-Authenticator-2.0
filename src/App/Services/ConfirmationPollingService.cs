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
                        catch (Exception fetchEx) when (LooksLikeExpiredSession(fetchEx))
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

        /// <summary>SteamAuth's FetchConfirmationInternal throws a plain Exception (not a typed
        /// one) for both "needauth" and any non-success response from Steam - so message
        /// matching is the only signal available without changing SteamAuth's public exception
        /// shape. Kept narrow (only the two known "your session is bad" messages) so unrelated
        /// failures - bad device id, network errors, Steam outages - are surfaced normally
        /// instead of triggering an unnecessary re-login attempt.</summary>
        private static bool LooksLikeExpiredSession(Exception ex) =>
            ex.Message != null &&
            (ex.Message.Contains("Needs Authentication", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("Invalid Access Token", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("access_token", StringComparison.OrdinalIgnoreCase));

        public void Dispose() => Stop();
    }
}
