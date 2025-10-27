using System.Net.Http.Json;
using System.Text.Json;

namespace WhatsAppAdmin.Services
{
    public class WhapiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public WhapiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _apiKey = "KTfBVmbnACOqJtO0wS0zkzpl8lHIjYED"; // replace with your sandbox key
        }

        public async Task<string?> CreateGroupAsync(string groupName, IEnumerable<string> phoneNumbers)
        {
            var url = "https://gate.whapi.cloud/groups";

            var payload = new
            {
                subject = groupName,
                participants = phoneNumbers
                    .Select(p => p.Replace("+", "").Trim()) // just digits, no domain
                    .ToList()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request);

            var result = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response: {result}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Whapi error: {response.StatusCode}\n{result}");
            }

            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetString();
            }

            return null;
        }
    }
}
