using System.Text.Json;
using MudBlazor;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Shared;
using WhatsAppAdmin.Helpers;

namespace WhatsAppAdmin.Services
{
    public class GroupSyncService : IDisposable
    {
        private readonly ImportStateService _importState;
        private readonly WhapiService _whapi;
        private readonly OverlayService _overlay;
        private readonly ISnackbar _snackbar;

        private List<WhatsAppGroup> _groups = [];

        public GroupSyncService(
            ImportStateService importState,
            WhapiService whapi,
            OverlayService overlay,
            ISnackbar snackbar)
        {
            _importState = importState;
            _whapi = whapi;
            _overlay = overlay;
            _snackbar = snackbar;

            _importState.OnChange += OnImportChanged;
        }

        public async Task<List<WhatsAppGroup>> InitializeAsync()
        {
            if (_importState.HasGroups)
                _groups = WhatsAppGroupMapper.FromImported(_importState.Groups);

            _groups = await RefreshAsync();
            return _groups;
        }

        public async Task<List<WhatsAppGroup>> RefreshAsync(bool afterDeleteAll = false)
        {
            await Task.Delay(1000);
            return await _overlay.RunAsync(async () =>
            {
                try
                {
                    // Load contacts and create phone-to-name map
                    var contacts = await LoadContactsAsync();
                    var phoneToName = contacts
                        .Where(c => !string.IsNullOrWhiteSpace(c.PhoneNumber))
                        .ToDictionary(
                            c => MainLayout.NormalizePhone(c.PhoneNumber),
                            c => c.Name,
                            StringComparer.OrdinalIgnoreCase // ✅ handles case-insensitive phone keys
                        );

                    // Fetch groups from WhatsApp API
                    var apiGroups = await _whapi.GetAllGroupsAsync();

                    // Convert and enrich with contact names
                    _groups = WhatsAppGroupMapper.FromApi(apiGroups, _groups, afterDeleteAll)
                        .Select(g =>
                        {
                            foreach (var u in g.Users)
                            {
                                var normalized = MainLayout.NormalizePhone(u.PhoneNumber);
                                if (phoneToName.TryGetValue(normalized, out var name))
                                    u.Name = name;
                            }
                            return g;
                        })
                        .ToList();

                    return _groups;
                }
                catch (Exception ex)
                {
                    _snackbar.Add($"❌ Failed to refresh groups: {ex.Message}", Severity.Error, cfg =>
                    {
                        cfg.RequireInteraction = true;
                        cfg.ShowCloseIcon = true;
                    });

                    // Return last-known groups (empty list if none)
                    return _groups;
                }
            }, "Loading");
        }


        private async Task<List<User>> LoadContactsAsync()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "contacts.json");
            if (!File.Exists(path)) return [];

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<User>>(json) ?? [];
        }

        private async void OnImportChanged()
        {
            await _overlay.RunAsync(async () =>
            {
                if (_importState.HasGroups)
                {
                    var imported = WhatsAppGroupMapper.FromImported(_importState.Groups);
                    _groups = await MergeImportedAsync(imported);
                }
                else
                {
                    _groups = await RefreshAsync();
                }
            }, "Importing");
        }

        private async Task<List<WhatsAppGroup>> MergeImportedAsync(IEnumerable<WhatsAppGroup> imported)
        {
            var apiGroups = await _whapi.GetAllGroupsAsync();
            var existing = WhatsAppGroupMapper.FromApi(apiGroups, _groups);

            var merged = new List<WhatsAppGroup>();

            foreach (var ig in imported)
            {
                var match = existing.FirstOrDefault(g =>
                    string.Equals(g.Name, ig.Name, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    ig.IsNew = true;
                    merged.Add(ig);
                }
                else
                {
                    var existingPhones = match.Users
                        .Select(u => MainLayout.NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    var importedPhones = ig.Users
                        .Select(u => MainLayout.NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    match.ToAdd = ig.Users
                        .Where(u => !existingPhones.Contains(MainLayout.NormalizePhone(u.PhoneNumber)))
                        .ToList();

                    match.ToRemove = match.Users
                        .Where(u => !importedPhones.Contains(MainLayout.NormalizePhone(u.PhoneNumber)))
                        .ToList();

                    merged.Add(match);
                }
            }

            merged.AddRange(existing
                .Where(g => !imported.Any(i => string.Equals(i.Name, g.Name, StringComparison.OrdinalIgnoreCase))));

            return merged;
        }

        public async Task SyncAsync()
        {
            await _overlay.RunAsync(async () =>
            {
                foreach (var g in _groups)
                {
                    try
                    {
                        if (g.IsNew)
                        {
                            var phones = g.Users.Select(u => u.PhoneNumber).ToList();
                            g.Id = await _whapi.CreateGroupAsync(g.Name, phones);
                            _snackbar.Add($"✅ Created group '{g.Name}'", Severity.Success);
                        }
                        else
                        {
                            if (g.ToAdd.Any())
                            {
                                await _whapi.AddParticipantsAsync(g.Id, g.ToAdd.Select(u => u.PhoneNumber));
                                _snackbar.Add($"✅ Added {g.ToAdd.Count} users to '{g.Name}'", Severity.Success);
                            }

                            if (g.ToRemove.Any())
                            {
                                try
                                {
                                    await _whapi.RemoveParticipantsAsync(g.Id, g.ToRemove.Select(u => u.PhoneNumber));
                                    _snackbar.Add($"⚠️ Removed {g.ToRemove.Count} users from '{g.Name}'", Severity.Warning);
                                }
                                catch (Exception ex)
                                {
                                    _snackbar.Add(ex.Message, Severity.Error);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _snackbar.Add($"❌ Error syncing '{g.Name}': {ex.Message}", Severity.Error);
                    }
                }

                _snackbar.Add("✅ Sync complete.", Severity.Success);
            }, "Syncing WhatsApp Groups");
        }

        public async Task DeleteAllAsync()
        {
            await _overlay.RunAsync(async () =>
            {
                int total = _groups.Count;
                int current = 0;

                foreach (var g in _groups)
                {
                    current++;
                    try
                    {
                        await _whapi.SafeDeleteGroupAsync(g.Id);
                        _snackbar.Add($"✅ Deleted {current}/{total}: {g.Name}", Severity.Error);
                    }
                    catch (Exception ex)
                    {
                        _snackbar.Add($"❌ Failed to delete {g.Name}: {ex.Message}", Severity.Error);
                    }

                    await Task.Delay(300);
                }

                _snackbar.Add("All groups processed.", Severity.Success);
            }, "Deleting all WhatsApp groups");
        }

        public void Dispose() => _importState.OnChange -= OnImportChanged;
    }
}
