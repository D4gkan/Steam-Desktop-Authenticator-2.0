using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;

namespace SteamAuth
{
    /// <summary>
    /// Class to help align system time with the Steam server time. Not super advanced; probably not taking some things into account that it should.
    /// Necessary to generate up-to-date codes. In general, this will have an error of less than a second, assuming Steam is operational.
    /// </summary>
    public class TimeAligner
    {
        private static bool _aligned = false;
        private static int _timeDifference = 0;

        public static long GetSteamTime()
        {
            if (!TimeAligner._aligned)
            {
                TimeAligner.AlignTime();
            }
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _timeDifference;
        }

        public static async Task<long> GetSteamTimeAsync()
        {
            if (!TimeAligner._aligned)
            {
                await TimeAligner.AlignTimeAsync();
            }
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _timeDifference;
        }

        public static void AlignTime()
        {
            // Genuinely synchronous (not async-wrapped) to avoid deadlocking callers on a
            // UI thread with a synchronization context - HttpClient.Send is a true sync call.
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var client = new HttpClient();
            try
            {
                using var content = new StringContent("steamid=0", Encoding.UTF8, "application/x-www-form-urlencoded");
                using var request = new HttpRequestMessage(HttpMethod.Post, APIEndpoints.TWO_FACTOR_TIME_QUERY) { Content = content };
                using var httpResponse = client.Send(request);
                httpResponse.EnsureSuccessStatusCode();
                string response;
                using (var reader = new System.IO.StreamReader(httpResponse.Content.ReadAsStream()))
                {
                    response = reader.ReadToEnd();
                }
                TimeQuery query = JsonConvert.DeserializeObject<TimeQuery>(response);
                TimeAligner._timeDifference = (int)(query.Response.ServerTime - currentTime);
                TimeAligner._aligned = true;
            }
            catch (HttpRequestException)
            {
                return;
            }
        }

        public static async Task AlignTimeAsync()
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var client = new HttpClient();
            try
            {
                using var content = new StringContent("steamid=0", Encoding.UTF8, "application/x-www-form-urlencoded");
                using var httpResponse = await client.PostAsync(APIEndpoints.TWO_FACTOR_TIME_QUERY, content);
                httpResponse.EnsureSuccessStatusCode();
                string response = await httpResponse.Content.ReadAsStringAsync();
                TimeQuery query = JsonConvert.DeserializeObject<TimeQuery>(response);
                TimeAligner._timeDifference = (int)(query.Response.ServerTime - currentTime);
                TimeAligner._aligned = true;
            }
            catch (HttpRequestException)
            {
                return;
            }
        }

        internal class TimeQuery
        {
            [JsonProperty("response")]
            internal TimeQueryResponse Response { get; set; }

            internal class TimeQueryResponse
            {
                [JsonProperty("server_time")]
                public long ServerTime { get; set; }
            }

        }
    }
}
