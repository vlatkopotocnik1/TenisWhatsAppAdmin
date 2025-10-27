using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace WhatsAppAdmin.Services
{
    public class WhapiService
    {
        private readonly HttpClient _httpClient;
        private  static readonly string _apiKey = "KTfBVmbnACOqJtO0wS0zkzpl8lHIjYED"; // replace with your sandbox key

        public WhapiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public static async Task<string> CreateGroupAsync(string groupName, IEnumerable<string> participants)
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://gate.whapi.cloud");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var payload = new
            {
                subject = groupName,
                participants = participants.Select(p => p.Replace("+", "")).ToList()
            };

            var response = await client.PostAsJsonAsync("/groups", payload);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to create group: {json}");

            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("id").GetString();

            return id ?? string.Empty;
        }


        public static async Task DeleteGroupAsync(string groupId)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            client.BaseAddress = new Uri("https://gate.whapi.cloud");

            var response = await client.DeleteAsync($"/groups/{groupId}");

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to delete group ({response.StatusCode}): {content}");
            }
        }
    }
}
