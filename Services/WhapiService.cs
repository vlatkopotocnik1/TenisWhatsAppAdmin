using System.Net.Http.Headers;
using System.Text;
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

        public async Task<List<JsonElement>> GetAllGroupsAsync()
        {
            //// Remove when PROD
            //return await Task.FromResult(new List<JsonElement>());
            var resp = await _http.GetAsync("/groups");
            await EnsureSuccess(resp);

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var groups = new List<JsonElement>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    groups.Add(item.Clone()); // ✅ Clone to keep data alive after disposal
                }
            }
            else if (doc.RootElement.TryGetProperty("groups", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    groups.Add(item.Clone()); // ✅ Clone each item
                }
            }

            return groups;
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

        public async Task SafeDeleteGroupAsync(string groupId)
        {
            _logger.LogInformation("🔍 Checking group {GroupId} before deletion...", groupId);

            var info = await GetGroupInfoAsync(groupId);
            if (!info.HasValue)
            {
                _logger.LogWarning("⚠️ Group {GroupId} not found.", groupId);
                return;
            }

            var participants = new List<string>();
            string? creatorId = null;

            if (info.Value.TryGetProperty("participants", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    string? id = null;

                    if (p.ValueKind == JsonValueKind.String)
                        id = p.GetString();
                    else if (p.ValueKind == JsonValueKind.Object &&
                             p.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        id = idEl.GetString();

                    if (!string.IsNullOrWhiteSpace(id))
                        participants.Add(Normalize(id));
                }
            }

            // Optional: get the creator (for your own logic)
            if (info.Value.TryGetProperty("created_by", out var creatorEl) && creatorEl.ValueKind == JsonValueKind.String)
                creatorId = Normalize(creatorEl.GetString()!);

            _logger.LogInformation("👥 Found {Count} participant(s) in group {GroupId}", participants.Count, groupId);

            // ✅ CASE 1: Group has only one participant (the creator)
            if (participants.Count == 1 && participants.First() == creatorId)
            {
                _logger.LogWarning("🚫 Cannot remove creator when they are the only member in group {GroupId}. Skipping removal.", groupId);
                await LeaveGroupAsync(groupId);
                return;
            }

            // ✅ CASE 2: Group has multiple members → remove all
            const int batchSize = 50;
            for (int i = 0; i < participants.Count; i += batchSize)
            {
                var batch = participants.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation("Removing {Count} participants from group {GroupId}...", batch.Count, groupId);
                await RemoveParticipantsAsync(groupId, batch);
            }

            // ✅ Finally, leave the group yourself
            await LeaveGroupAsync(groupId);
            _logger.LogInformation("✅ Left group {GroupId}", groupId);
        }


        public async Task AddParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var payload = new { participants = participants.Select(Normalize).ToList() };
            var response = await _http.PostAsJsonAsync($"/groups/{groupId}/participants", payload);
            await EnsureSuccess(response);
        }

        public async Task UpdateGroupAsync(string groupId, string newName)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("groupId is required", nameof(groupId));

            var payload = new
            {
                subject = newName
            };

            // PUT /groups/{groupId}
            var response = await _http.PutAsJsonAsync($"/groups/{groupId}", payload);
            await EnsureSuccess(response);
        }

        public async Task AddUserToGroupAsync(string groupName, string phoneNumber)
        {
            var groups = await GetAllGroupsAsync();
            var group = groups.FirstOrDefault(g => g.GetProperty("name").GetString() == groupName);

            if (group.ValueKind == JsonValueKind.Undefined)
                throw new Exception("Group not found");

            var groupId = group.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(groupId))
                throw new Exception("Invalid group ID");

            // Format the request payload
            var payload = new
            {
                participants = new[] { phoneNumber } // WhatsApp ID (e.g., "1234567890@c.us")
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Correct API endpoint per the docs
            var response = await _http.PostAsync($"/groups/{groupId}/participants", content);

            if (!response.IsSuccessStatusCode)
            {
                var msg = await response.Content.ReadAsStringAsync();
                throw new Exception($"API error: {msg}");
            }
        }


        public async Task<string?> GetContactNameAsync(string phone)
        {
            // TO MANY API CALL FOR TESTING
            //phone = Normalize(phone);
            //var resp = await _http.GetAsync($"/contacts/{phone}");
            //if (!resp.IsSuccessStatusCode)
            //    return null;

            //using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            //if (doc.RootElement.TryGetProperty("pushName", out var nameEl))
            //    return nameEl.GetString();
            //if (doc.RootElement.TryGetProperty("name", out var altName))
            //    return altName.GetString();

            return await Task.FromResult(phone);
        }
    }
}
