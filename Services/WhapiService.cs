using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace WhatsAppAdmin.Services
{
    public class WhapiService
    {
        private readonly HttpClient _http;
        private readonly ILogger<WhapiService> _logger;

        private readonly string _apiKey;
        private const string BaseUrl = "https://gate.whapi.cloud";

        public WhapiService(HttpClient http, IConfiguration cfg, ILogger<WhapiService> logger)
        {
            _http = http;
            _logger = logger;

            _apiKey = cfg["Whapi:ApiKey"]
                      ?? throw new InvalidOperationException("Whapi:ApiKey not configured.");

            _http.BaseAddress = new Uri(BaseUrl);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        // --- Helpers ---

        private static string Normalize(string number) =>
            number.Replace("+", "").Replace("@c.us", "").Trim();

        private static async Task EnsureSuccess(HttpResponseMessage resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Request failed ({(int)resp.StatusCode} {resp.ReasonPhrase}): {text}");
            }
        }

        // --- API Methods ---

        public async Task<string> CreateGroupAsync(string groupName, IEnumerable<string> participants)
        {
            var payload = new
            {
                subject = groupName,
                participants = participants.Select(Normalize).ToList()
            };

            var response = await _http.PostAsJsonAsync("/groups", payload);
            await EnsureSuccess(response);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.TryGetProperty("id", out var idEl)
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
        }

        public async Task RemoveParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var payload = new { participants = participants.Select(Normalize).ToList() };
            var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/groups/{groupId}/participants")
            {
                Content = JsonContent.Create(payload)
            });

            await EnsureSuccess(response);
        }

        public async Task LeaveGroupAsync(string groupId)
        {
            var resp = await _http.DeleteAsync($"/groups/{groupId}");
            await EnsureSuccess(resp);
        }

        public async Task<JsonElement?> GetGroupInfoAsync(string groupId)
        {
            var resp = await _http.GetAsync($"/groups/{groupId}");
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        public async Task DeleteGroupFullyAsync(string groupId)
        {
            _logger.LogInformation("Deleting group {GroupId} fully...", groupId);

            var participants = new List<string>();

            var info = await GetGroupInfoAsync(groupId);
            if (info.HasValue && info.Value.TryGetProperty("participants", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                        participants.Add(Normalize(p.GetString()!));
                    else if (p.ValueKind == JsonValueKind.Object &&
                             p.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        participants.Add(Normalize(idEl.GetString()!));
                }
            }

            const int batchSize = 50;
            for (int i = 0; i < participants.Count; i += batchSize)
            {
                var batch = participants.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation("Removing {Count} participants from group {GroupId}...", batch.Count, groupId);
                await RemoveParticipantsAsync(groupId, batch);
            }

            await LeaveGroupAsync(groupId);
            _logger.LogInformation("Left group {GroupId}", groupId);
        }
    }
}
