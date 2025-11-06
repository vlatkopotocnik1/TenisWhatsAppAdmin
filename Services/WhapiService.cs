using System.Net.Http.Headers;
using System.Text.Json;

namespace WhatsAppAdmin.Services
{
    public class WhapiService
    {
        private readonly HttpClient _http;
        private readonly ILogger<WhapiService> _logger;

        private readonly string _apiKey;
        private const string BaseUrl = "https://gate.whapi.cloud";

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public WhapiService(HttpClient http, IConfiguration cfg, ILogger<WhapiService> logger)
        {
            _http = http;
            _logger = logger;
            _apiKey = cfg["Whapi:ApiKey"]
                      ?? throw new InvalidOperationException("Whapi:ApiKey not configured.");

            _http.BaseAddress = new Uri(BaseUrl);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<List<JsonElement>> GetAllGroupsAsync()
        {
            var resp = await _http.GetAsync("/groups");
            await EnsureSuccess(resp);

            var stream = await resp.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts);

            if (root.ValueKind == JsonValueKind.Array)
                return [.. root.EnumerateArray().Select(x => x.Clone())];

            if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                return [.. groups.EnumerateArray().Select(x => x.Clone())];

            return [];
        }

        public async Task<string> CreateGroupAsync(string name, IEnumerable<string> participants)
        {
            var payload = new
            {
                subject = name,
                participants = participants.Select(Normalize).ToList()
            };

            var resp = await _http.PostAsJsonAsync("/groups", payload, JsonOpts);
            await EnsureSuccess(resp);

            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var idEl)
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
        }

        public async Task UpdateGroupAsync(string groupId, string newName)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("Group ID is required.", nameof(groupId));

            var payload = new { subject = newName };
            var resp = await _http.PutAsJsonAsync($"/groups/{groupId}", payload, JsonOpts);
            await EnsureSuccess(resp);
        }

        public async Task AddParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var payload = new { participants = participants.Select(Normalize).ToList() };
            var resp = await _http.PostAsJsonAsync($"/groups/{groupId}/participants", payload, JsonOpts);
            await EnsureSuccess(resp);
        }

        public async Task RemoveParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var info = await GetGroupInfoAsync(groupId);
            if (!info.HasValue)
            {
                _logger.LogWarning("Group {GroupId} not found.", groupId);
                return;
            }

            var creator = info.Value.TryGetProperty("created_by", out var creatorEl)
                ? Normalize(creatorEl.GetString() ?? string.Empty)
                : null;

            if (participants.Contains(creator) && participants.Count() == 1)
            {
                throw new InvalidOperationException($"❌ Creator cant be removed from group");
            }

            participants = [.. participants.Where(p => p != creator)];

            var payload = new { participants = participants.Select(Normalize).ToList() };
            var req = new HttpRequestMessage(HttpMethod.Delete, $"/groups/{groupId}/participants")
            {
                Content = JsonContent.Create(payload, options: JsonOpts)
            };

            var resp = await _http.SendAsync(req);
            await EnsureSuccess(resp);
        }

        public async Task LeaveGroupAsync(string groupId)
        {
            var resp = await _http.DeleteAsync($"/groups/{groupId}");
            await EnsureSuccess(resp);
        }

        public async Task<JsonElement?> GetGroupInfoAsync(string groupId)
        {
            var resp = await _http.GetAsync($"/groups/{groupId}");
            if (!resp.IsSuccessStatusCode) return null;

            var stream = await resp.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts);
            return root.Clone();
        }

        public async Task SafeDeleteGroupAsync(string groupId)
        {
            _logger.LogInformation("Deleting group {GroupId} safely...", groupId);

            var info = await GetGroupInfoAsync(groupId);
            if (!info.HasValue)
            {
                _logger.LogWarning("Group {GroupId} not found.", groupId);
                return;
            }

            var participants = ExtractParticipants(info.Value);
            var creator = info.Value.TryGetProperty("created_by", out var creatorEl)
                ? Normalize(creatorEl.GetString() ?? string.Empty)
                : null;

            if (participants.Count == 1 && participants.First() == creator)
            {
                _logger.LogWarning("Only creator in group {GroupId}; leaving group.", groupId);
                //await LeaveGroupAsync(groupId);
                throw new InvalidOperationException("There’s only the group creator left, and they can’t be removed.");
            }

            participants.Remove(creator);

            const int batch = 50;
            for (int i = 0; i < participants.Count; i += batch)
            {
                var chunk = participants.Skip(i).Take(batch).ToList();
                await RemoveParticipantsAsync(groupId, chunk);
                _logger.LogInformation("Removed {Count} members from {GroupId}", chunk.Count, groupId);
            }

            //await LeaveGroupAsync(groupId);
            _logger.LogInformation("Left group {GroupId}", groupId);
        }

        public async Task AddUserToGroupAsync(string groupName, string phone)
        {
            var groups = await GetAllGroupsAsync();
            var group = groups.FirstOrDefault(g => g.TryGetProperty("name", out var n) && n.GetString() == groupName);

            if (group.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException($"Group '{groupName}' not found.");

            var groupId = group.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(groupId))
                throw new InvalidOperationException("Invalid group ID.");

            var payload = new { participants = new[] { Normalize(phone) } };
            var resp = await _http.PostAsJsonAsync($"/groups/{groupId}/participants", payload, JsonOpts);
            await EnsureSuccess(resp);
        }

        // send a text message to a group (groupId or groupName depending on API)
        public async Task SendMessageToGroupAsync(string groupId, string message)
        {
            var payload = new { id = groupId, message };
            var resp = await _http.PostAsJsonAsync("/groups/sendMessage", payload);
            await EnsureSuccess(resp);
        }

        // send message to an individual phone
        public async Task SendMessageToPhoneAsync(string phone, string message)
        {
            var payload = new { to = phone, message };
            var resp = await _http.PostAsJsonAsync("/messages/send", payload);
            await EnsureSuccess(resp);
        }

        // send message to many phones (batch)
        public async Task SendBulkMessagesAsync(IEnumerable<string> phones, string message)
        {
            // implement batching if API has limits
            foreach (var chunk in phones.Chunk(50))
            {
                var payload = chunk.Select(p => new { to = p, message }).ToList();
                await _http.PostAsJsonAsync("/messages/batch", payload);
            }
        }

        private static string Normalize(string number)
            => number.Replace("+", "").Replace("@c.us", "").Trim();

        private static List<string> ExtractParticipants(JsonElement info)
        {
            var list = new List<string>();

            if (!info.TryGetProperty("participants", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var p in arr.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.String)
                    list.Add(Normalize(p.GetString() ?? ""));
                else if (p.ValueKind == JsonValueKind.Object &&
                         p.TryGetProperty("id", out var idEl) &&
                         idEl.ValueKind == JsonValueKind.String)
                    list.Add(Normalize(idEl.GetString() ?? ""));
            }

            return list;
        }

        private async Task EnsureSuccess(HttpResponseMessage resp)
        {
            if (resp.IsSuccessStatusCode) return;

            var text = await resp.Content.ReadAsStringAsync();
            _logger.LogError("Request failed: {Code} {Reason}. Body: {Body}",
                (int)resp.StatusCode, resp.ReasonPhrase, text);

            throw new HttpRequestException(
                $"Request failed ({(int)resp.StatusCode} {resp.ReasonPhrase}): {text}");
        }
    }
}
