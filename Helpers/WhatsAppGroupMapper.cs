using System.Text.Json;
using WhatsAppAdmin.Models;

namespace WhatsAppAdmin.Helpers
{
    public static class WhatsAppGroupMapper
    {
        public static List<WhatsAppGroup> FromImported(IEnumerable<WhatsAppGroup> imported)
        {
            return imported.Select(ig => new WhatsAppGroup
            {
                Id = ig.Id ?? Guid.NewGuid().ToString(),
                Name = ig.Name ?? string.Empty,
                Users = ig.Users.Select(u => new User
                {
                    Id = u.Id == Guid.Empty ? Guid.NewGuid() : u.Id,
                    Name = u.Name ?? string.Empty,
                    Rating = 0,
                    Notes = string.Empty,
                    PhoneNumber = u.PhoneNumber ?? string.Empty
                }).ToList()
            }).ToList();
        }

        public static List<WhatsAppGroup> FromApi(
            List<JsonElement> apiGroups,
            List<WhatsAppGroup>? existing = null,
            bool afterDeleteAll = false)
        {
            var result = new List<WhatsAppGroup>();
            existing ??= [];

            foreach (var g in apiGroups)
            {
                var id = g.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                var name = g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "Unnamed"
                    : existing.FirstOrDefault(x => x.Id == id)?.Name ?? "Unnamed";

                var users = new List<User>();

                if (g.TryGetProperty("participants", out var p) && p.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in p.EnumerateArray())
                    {
                        string phone = string.Empty;
                        string rank = string.Empty;
                        string username = string.Empty;

                        if (u.ValueKind == JsonValueKind.String)
                            phone = u.GetString() ?? string.Empty;
                        else if (u.ValueKind == JsonValueKind.Object)
                        {
                            phone = GetStringProp(u, "id") ?? GetStringProp(u, "phone") ?? string.Empty;
                            rank = GetStringProp(u, "rank") ?? string.Empty;
                            username = GetStringProp(u, "username") ?? string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(phone))
                            users.Add(new User { PhoneNumber = phone, Rank = rank, Name = username });
                    }
                }
                else if (!afterDeleteAll)
                {
                    var existingGroup = existing.FirstOrDefault(x => x.Id == id || x.Name == name);
                    if (existingGroup != null)
                        users = existingGroup.Users;
                }

                result.Add(new WhatsAppGroup { Id = id, Name = name, Users = users });
            }

            return result;
        }

        private static string? GetStringProp(in JsonElement el, string name)
        {
            return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
    }
}
