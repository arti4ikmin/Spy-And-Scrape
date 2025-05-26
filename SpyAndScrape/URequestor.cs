using System.Text;

namespace SpyAndScrape
{
    public class URequestor
    {
        private readonly HttpClient _httpClient;

        public URequestor()
        {
            _httpClient = new HttpClient();
        }

        public async Task<string> GetAsync(string url, Dictionary<string, string>? queryParams = null, Dictionary<string, string> headers = null)
        {

            // construct with query parameters if provided
            if (queryParams != null && queryParams.Count > 0)
            {
                var queryString = string.Join("&", queryParams.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
                url = $"{url}?{queryString}";
            }

            var req = new HttpRequestMessage(HttpMethod.Get, url);


            foreach (var header in headers)
            {
                req.Headers.Add(header.Key, header.Value);
            }
        

            var res = await _httpClient.SendAsync(req);

            res.EnsureSuccessStatusCode();

            return await res.Content.ReadAsStringAsync();
            
        }

        // post UNIVERSAL
        public async Task<string> PostAsync(string url, string? rawJBody = null, Dictionary<string, string>? headers = null)
        {

            HttpContent content = null;
            if (!string.IsNullOrEmpty(rawJBody))
            {
                content = new StringContent(rawJBody, Encoding.UTF8, "application/json");
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };


            foreach (var header in headers)
            {
                if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    req.Headers.Add(header.Key, header.Value);
                }
            }
            

            using var res = await _httpClient.SendAsync(req);

            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                return $"err: {res.StatusCode} ({res.ReasonPhrase}) - {err}";
            }

            return await res.Content.ReadAsStringAsync();

        }
    }
}
