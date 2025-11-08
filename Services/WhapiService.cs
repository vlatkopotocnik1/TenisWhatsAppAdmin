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
            _apiKey = cfg["Whapi:ApiKey"] ?? throw new InvalidOperationException("Whapi:ApiKey not configured.");

            // BaseAddress and auth normally configured in DI AddHttpClient, but ensure they're set
            if (_http.BaseAddress == null)
                _http.BaseAddress = new Uri(BaseUrl);

            if (!_http.DefaultRequestHeaders.Contains("Authorization"))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<List<JsonElement>> GetAllGroupsAsync()
        {
            _logger.LogInformation("GetAllGroupsAsync - calling /groups");

            var resp = await _http.GetAsync("/groups");
            await EnsureSuccess(resp);

            var stream = await resp.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts);

            // heavy diagnostic: log response size at Debug
            try
            {
                if (resp.Content.Headers.ContentLength.HasValue)
                {
                    _logger.LogDebug("GetAllGroupsAsync - response Content-Length: {Len} bytes", resp.Content.Headers.ContentLength.Value);
                }
                else
                {
                    // If content-length isn't present, log approximate length of the string (debug only)
                    var text = await resp.Content.ReadAsStringAsync();
                    _logger.LogDebug("GetAllGroupsAsync - response body length (approx): {Len} bytes", text.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllGroupsAsync - could not compute response length");
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var count = root.GetArrayLength();
                _logger.LogInformation("GetAllGroupsAsync - returned {Count} groups (array)", count);
                return root.EnumerateArray().Select(x => x.Clone()).ToList();
            }

            if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                var count = groups.GetArrayLength();
                _logger.LogInformation("GetAllGroupsAsync - returned {Count} groups (groups property)", count);
                return groups.EnumerateArray().Select(x => x.Clone()).ToList();
            }

            _logger.LogWarning("GetAllGroupsAsync - unexpected payload kind {Kind}", root.ValueKind);
            return new List<JsonElement>();
        }

        public async Task<string> CreateGroupAsync(string name, IEnumerable<string> participants)
        {
            var masked = participants.Select(p => MaskPhone(p)).ToList();
            _logger.LogInformation("CreateGroupAsync - creating group '{GroupName}' with {Count} participants", name, masked.Count);
            _logger.LogDebug("CreateGroupAsync - masked participants: {@Participants}", masked);

            var payload = new
            {
                subject = name,
                participants = participants.Select(Normalize).ToList()
            };

            var resp = await _http.PostAsJsonAsync("/groups", payload, JsonOpts);
            await EnsureSuccess(resp);

            var json = await resp.Content.ReadAsStringAsync();
            _logger.LogDebug("CreateGroupAsync - response JSON length: {Len}", json?.Length ?? 0);

            var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;

            _logger.LogInformation("CreateGroupAsync - created group '{GroupName}' id={GroupId}", name, id);
            return id;
        }

        public async Task UpdateGroupAsync(string groupId, string newName)
        {
            _logger.LogInformation("UpdateGroupAsync - updating group {GroupId} -> {NewName}", groupId, newName);

            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("Group ID is required.", nameof(groupId));

            var payload = new { subject = newName };
            var resp = await _http.PutAsJsonAsync($"/groups/{groupId}", payload, JsonOpts);
            await EnsureSuccess(resp);

            _logger.LogInformation("UpdateGroupAsync - updated group {GroupId}", groupId);
        }

        public async Task AddParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var masked = participants.Select(p => MaskPhone(p)).ToList();
            _logger.LogInformation("AddParticipantsAsync - group={GroupId} adding {Count} participants", groupId, masked.Count);
            _logger.LogDebug("AddParticipantsAsync - masked participants: {@Participants}", masked);

            var payload = new { participants = participants.Select(Normalize).ToList() };
            var resp = await _http.PostAsJsonAsync($"/groups/{groupId}/participants", payload, JsonOpts);
            await EnsureSuccess(resp);

            _logger.LogInformation("AddParticipantsAsync - group={GroupId} added {Count} participants", groupId, masked.Count);
        }

        public async Task RemoveParticipantsAsync(string groupId, IEnumerable<string> participants)
        {
            var masked = participants.Select(p => MaskPhone(p)).ToList();
            _logger.LogInformation("RemoveParticipantsAsync - group={GroupId} removing {Count} participants", groupId, masked.Count);
            _logger.LogDebug("RemoveParticipantsAsync - masked participants: {@Participants}", masked);

            var info = await GetGroupInfoAsync(groupId);
            if (!info.HasValue)
            {
                _logger.LogWarning("RemoveParticipantsAsync - group {GroupId} not found", groupId);
                return;
            }

            var creator = info.Value.TryGetProperty("created_by", out var creatorEl) ? Normalize(creatorEl.GetString() ?? string.Empty) : null;

            if (participants.Contains(creator) && participants.Count() == 1)
            {
                _logger.LogWarning("RemoveParticipantsAsync - attempt to remove only creator from group {GroupId}", groupId);
                throw new InvalidOperationException("Creator can't be removed from group");
            }

            var filtered = participants.Where(p => p != creator).ToList();

            var payload = new { participants = filtered.Select(Normalize).ToList() };
            var req = new HttpRequestMessage(HttpMethod.Delete, $"/groups/{groupId}/participants")
            {
                Content = JsonContent.Create(payload, options: JsonOpts)
            };

            var resp = await _http.SendAsync(req);
            await EnsureSuccess(resp);

            _logger.LogInformation("RemoveParticipantsAsync - group={GroupId} removed {Count}", groupId, filtered.Count);
        }

        public async Task LeaveGroupAsync(string groupId)
        {
            _logger.LogInformation("LeaveGroupAsync - leaving group {GroupId}", groupId);
            var resp = await _http.DeleteAsync($"/groups/{groupId}");
            await EnsureSuccess(resp);
            _logger.LogInformation("LeaveGroupAsync - left group {GroupId}", groupId);
        }

        public async Task<JsonElement?> GetGroupInfoAsync(string groupId)
        {
            _logger.LogDebug("GetGroupInfoAsync - retrieving info for {GroupId}", groupId);
            var resp = await _http.GetAsync($"/groups/{groupId}");
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetGroupInfoAsync - group {GroupId} returned {Status}", groupId, (int)resp.StatusCode);
                return null;
            }

            var stream = await resp.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts);
            _logger.LogDebug("GetGroupInfoAsync - got info for {GroupId} (kind={Kind})", groupId, root.ValueKind);
            return root.Clone();
        }

        public async Task SafeDeleteGroupAsync(string groupId)
        {
            _logger.LogInformation("SafeDeleteGroupAsync - deleting group {GroupId}", groupId);

            var info = await GetGroupInfoAsync(groupId);
            if (!info.HasValue)
            {
                _logger.LogWarning("SafeDeleteGroupAsync - group {GroupId} not found", groupId);
                return;
            }

            var participants = ExtractParticipants(info.Value);
            var creator = info.Value.TryGetProperty("created_by", out var creatorEl) ? Normalize(creatorEl.GetString() ?? string.Empty) : null;

            if (participants.Count == 1 && participants.First() == creator)
            {
                _logger.LogWarning("SafeDeleteGroupAsync - only creator present in {GroupId}", groupId);
                throw new InvalidOperationException("There’s only the group creator left, and they can’t be removed.");
            }

            participants.Remove(creator);

            const int batch = 50;
            for (int i = 0; i < participants.Count; i += batch)
            {
                var chunk = participants.Skip(i).Take(batch).ToList();
                _logger.LogInformation("SafeDeleteGroupAsync - removing chunk of {Count} members from {GroupId}", chunk.Count, groupId);
                await RemoveParticipantsAsync(groupId, chunk);
                _logger.LogInformation("SafeDeleteGroupAsync - removed {Count} members from {GroupId}", chunk.Count, groupId);
            }

            _logger.LogInformation("SafeDeleteGroupAsync - finished removing members for {GroupId}", groupId);
        }

        public async Task AddUserToGroupAsync(string groupName, string phone)
        {
            _logger.LogInformation("AddUserToGroupAsync - adding user to group '{GroupName}'", groupName);
            _logger.LogDebug("AddUserToGroupAsync - masked phone: {Phone}", MaskPhone(phone));

            var groups = await GetAllGroupsAsync();
            var group = groups.FirstOrDefault(g => g.TryGetProperty("name", out var n) && n.GetString() == groupName);

            if (group.ValueKind == JsonValueKind.Undefined)
            {
                _logger.LogWarning("AddUserToGroupAsync - group '{GroupName}' not found", groupName);
                throw new InvalidOperationException($"Group '{groupName}' not found.");
            }

            var groupId = group.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(groupId))
            {
                _logger.LogWarning("AddUserToGroupAsync - invalid group id for '{GroupName}'", groupName);
                throw new InvalidOperationException("Invalid group ID.");
            }

            var payload = new { participants = new[] { Normalize(phone) } };
            var resp = await _http.PostAsJsonAsync($"/groups/{groupId}/participants", payload, JsonOpts);
            await EnsureSuccess(resp);

            _logger.LogInformation("AddUserToGroupAsync - added user to group '{GroupName}' (id={GroupId})", groupName, groupId);
        }

        public async Task SendMessageToGroupAsync(string groupId, string message)
        {
            _logger.LogInformation("SendMessageToGroupAsync - sending message to group {GroupId}", groupId);

            var payload = new { to = groupId, body = message };
            var resp = await _http.PostAsJsonAsync("/messages/text", payload);
            await EnsureSuccess(resp);

            _logger.LogInformation("SendMessageToGroupAsync - message sent to {GroupId}", groupId);
        }

        public async Task SendMessageToPhoneAsync(string phone, string message)
        {
            _logger.LogInformation("SendMessageToPhoneAsync - sending message to phone {PhoneMasked}", MaskPhone(phone));

            var payload = new { to = phone, message };
            var resp = await _http.PostAsJsonAsync("/messages/send", payload);
            await EnsureSuccess(resp);

            _logger.LogInformation("SendMessageToPhoneAsync - message sent to phone {PhoneMasked}", MaskPhone(phone));
        }

        public async Task SendBulkMessagesAsync(IEnumerable<string> phones, string message)
        {
            var count = phones.Count();
            _logger.LogInformation("SendBulkMessagesAsync - sending bulk message to {Count} phones (batched)", count);

            foreach (var chunk in phones.Chunk(50))
            {
                var maskedChunk = chunk.Select(MaskPhone).ToList();
                _logger.LogDebug("SendBulkMessagesAsync - masked chunk: {@Chunk}", maskedChunk);

                var payload = chunk.Select(p => new { to = p, message }).ToList();
                var resp = await _http.PostAsJsonAsync("/messages/batch", payload);
                await EnsureSuccess(resp);
            }

            _logger.LogInformation("SendBulkMessagesAsync - finished sending bulk messages to {Count} phones", count);
        }

        // ----------------- helpers -----------------

        private static string Normalize(string number)
            => number.Replace("+", "").Replace("@c.us", "").Trim();

        private static string MaskPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "N/A";
            var p = phone.Trim();
            if (p.Length <= 4) return new string('*', p.Length);
            return $"***{p[^4..]}";
        }

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
            _logger.LogError("External API request failed: {StatusCode} {Reason}. Body length: {Len}",
                (int)resp.StatusCode, resp.ReasonPhrase, text?.Length ?? 0);

            throw new HttpRequestException($"Request failed ({(int)resp.StatusCode} {resp.ReasonPhrase}): {text}");
        }
    }
}
