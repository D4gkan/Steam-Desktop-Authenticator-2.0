using SteamAuth;
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
    /// </summary>
    public class ConfirmationPollingService : IDisposable
    {
        public event Action<List<(AccountViewModel Owner, Confirmation Confirmation)>>? ConfirmationsUpdated;
        public event Action<AccountViewModel, Exception>? AccountPollFailed;

        private readonly Func<IEnumerable<AccountViewModel>> _accountsProvider;
        private Timer? _timer;
        private int _isPolling; // 0/1 guard against overlapping poll cycles

        public ConfirmationPollingService(Func<IEnumerable<AccountViewModel>> accountsProvider)
        {
            _accountsProvider = accountsProvider;
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
                        var confs = await acc.Account.FetchConfirmationsAsync();
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

        public void Dispose() => Stop();
    }
}
