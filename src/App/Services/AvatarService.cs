using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SteamDesktopAuthenticator.Core;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Services
{
    /// <summary>Fetches and caches Steam profile pictures by SteamID. Entirely best-effort: any
    /// failure (offline, Steam down, malformed profile, etc.) just means no avatar is shown and
    /// the caller falls back to its existing initial-letter placeholder. This never touches
    /// authentication, sessions, or maFiles, and never blocks the caller - every public method is
    /// async and swallows its own errors.</summary>
    public static class AvatarService
    {
        // Public profile XML endpoint. Deliberately not the Web API (ISteamUser/GetPlayerSummaries)
        // because that requires a Steam Web API key, and this project has no API key configuration
        // anywhere else (see SteamAuth.APIEndpoints) - keeping avatars key-less matches that.
        private const string ProfileXmlUrlFormat = "https://steamcommunity.com/profiles/{0}/?xml=1";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // In-memory cache so repeated UI refreshes (e.g. reordering, filtering) never re-hit disk
        // or network for an avatar already resolved this session.
        private static readonly ConcurrentDictionary<ulong, Bitmap?> _memoryCache = new();

        // In-flight de-duplication so multiple simultaneous requests for the same account (e.g.
        // the list rebuilding while a fetch is still pending) don't fire duplicate downloads.
        private static readonly ConcurrentDictionary<ulong, Task<Bitmap?>> _inFlight = new();

        private static string CacheDir => Path.Combine(Manifest.GetExecutableDir(), "maFiles", "avatar-cache");

        private static string CachePathFor(ulong steamId) => Path.Combine(CacheDir, steamId + ".jpg");

        /// <summary>Returns a cached avatar immediately if one is already known in-memory or on
        /// disk, without making a network request. Null means "unknown yet" - call
        /// <see cref="FetchAsync"/> to resolve it.</summary>
        public static Bitmap? GetCachedOnly(ulong steamId)
        {
            if (steamId == 0) return null;

            if (_memoryCache.TryGetValue(steamId, out var cached))
                return cached;

            try
            {
                string path = CachePathFor(steamId);
                if (File.Exists(path))
                {
                    var bmp = LoadBitmapSafely(path);
                    if (bmp != null)
                    {
                        _memoryCache[steamId] = bmp;
                        return bmp;
                    }
                }
            }
            catch
            {
                // Corrupt/unreadable cache file - fall through and let FetchAsync re-download it.
            }

            return null;
        }

        /// <summary>Resolves (and caches) the avatar for a SteamID. Always safe to call: network
        /// failures, malformed responses, and disk errors all result in a null return rather than
        /// a thrown exception, so callers never need a try/catch around this.</summary>
        public static Task<Bitmap?> FetchAsync(ulong steamId)
        {
            if (steamId == 0) return Task.FromResult<Bitmap?>(null);

            if (_memoryCache.TryGetValue(steamId, out var cached))
                return Task.FromResult(cached);

            return _inFlight.GetOrAdd(steamId, id => FetchInternalAsync(id));
        }

        private static async Task<Bitmap?> FetchInternalAsync(ulong steamId)
        {
            try
            {
                string cachePath = CachePathFor(steamId);
                if (File.Exists(cachePath))
                {
                    var cachedBmp = LoadBitmapSafely(cachePath);
                    if (cachedBmp != null)
                    {
                        _memoryCache[steamId] = cachedBmp;
                        return cachedBmp;
                    }
                }

                string? avatarUrl = await ResolveAvatarUrlAsync(steamId).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    _memoryCache[steamId] = null;
                    return null;
                }

                byte[] imageBytes = await _http.GetByteArrayAsync(avatarUrl).ConfigureAwait(false);

                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(cachePath, imageBytes).ConfigureAwait(false);

                var bitmap = LoadBitmapFromBytes(imageBytes);
                _memoryCache[steamId] = bitmap;
                return bitmap;
            }
            catch
            {
                // Any failure here (offline, DNS, timeout, bad image, disk full, etc.) just means
                // no avatar - never propagate, never affect the rest of the app.
                return null;
            }
            finally
            {
                _inFlight.TryRemove(steamId, out _);
            }
        }

        private static async Task<string?> ResolveAvatarUrlAsync(ulong steamId)
        {
            string xml = await _http.GetStringAsync(string.Format(ProfileXmlUrlFormat, steamId)).ConfigureAwait(false);

            // Lightweight regex extraction instead of a full XML parser: the profile XML feed is
            // simple and stable, and this avoids pulling in another dependency for one field.
            var match = Regex.Match(xml, "<avatarFull><!\\[CDATA\\[(?<url>[^\\]]+)\\]\\]></avatarFull>");
            if (!match.Success)
                match = Regex.Match(xml, "<avatarFull>(?<url>https?://[^<]+)</avatarFull>");

            return match.Success ? match.Groups["url"].Value : null;
        }

        private static Bitmap? LoadBitmapSafely(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                return new Bitmap(fs);
            }
            catch
            {
                // Corrupt cache file - remove it so future attempts re-download cleanly.
                try { File.Delete(path); } catch { /* best-effort cleanup */ }
                return null;
            }
        }

        private static Bitmap? LoadBitmapFromBytes(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Runs <paramref name="onLoaded"/> on the UI thread once the avatar resolves.
        /// Fire-and-forget helper for view models that just want to set a bindable property when
        /// (and if) an avatar becomes available.</summary>
        public static void FetchAndApply(ulong steamId, Action<Bitmap?> onLoaded)
        {
            var immediate = GetCachedOnly(steamId);
            if (immediate != null)
            {
                onLoaded(immediate);
                return;
            }

            _ = FetchAsync(steamId).ContinueWith(t =>
            {
                var result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                Dispatcher.UIThread.Post(() => onLoaded(result));
            }, TaskScheduler.Default);
        }
    }
}
