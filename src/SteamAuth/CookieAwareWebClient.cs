using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SteamAuth
{
    /// <summary>
    /// HttpClient-based replacement for the old WebClient-derived helper (WebClient is obsolete
    /// as of net8.0 - SYSLIB0014). Keeps the same call surface (CookieContainer, Encoding, Headers,
    /// DownloadStringTaskAsync, UploadValuesTaskAsync, UploadStringTaskAsync) used throughout
    /// SteamAuth so none of the call sites need to change.
    /// </summary>
    public class CookieAwareWebClient : IDisposable
    {
        public CookieContainer CookieContainer { get; set; } = new CookieContainer();
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public WebHeaderCollection Headers { get; } = new WebHeaderCollection();

        public async Task<string> DownloadStringTaskAsync(string address)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            CopyHeaders(request);
            using var response = await client.SendAsync(request);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Encoding.GetString(bytes);
        }

        public async Task<byte[]> UploadValuesTaskAsync(Uri address, string method, NameValueCollection data)
        {
            using var client = CreateClient();
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (string key in data.AllKeys)
            {
                if (key != null)
                    pairs.Add(new KeyValuePair<string, string>(key, data[key] ?? ""));
            }

            using var content = new FormUrlEncodedContent(pairs);
            using var request = new HttpRequestMessage(new HttpMethod(method), address) { Content = content };
            CopyHeaders(request);
            using var response = await client.SendAsync(request);
            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task<string> UploadStringTaskAsync(Uri address, string method, string data)
        {
            using var client = CreateClient();
            using var content = new StringContent(data, Encoding);
            content.Headers.ContentType = null; // set explicitly below if the caller provided one

            using var request = new HttpRequestMessage(new HttpMethod(method), address) { Content = content };
            CopyHeaders(request);
            using var response = await client.SendAsync(request);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Encoding.GetString(bytes);
        }

        private HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = CookieContainer ?? new CookieContainer(),
                UseCookies = true
            };
            return new HttpClient(handler);
        }

        private void CopyHeaders(HttpRequestMessage request)
        {
            foreach (string key in Headers.AllKeys)
            {
                if (key == null) continue;
                string value = Headers[key];
                if (value == null) continue;

                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Content != null)
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(value);
                    continue;
                }

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        public void Dispose()
        {
        }
    }
}
