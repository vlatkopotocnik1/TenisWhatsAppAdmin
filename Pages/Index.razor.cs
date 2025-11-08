using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Services;
using WhatsAppAdmin.Shared;
using WhatsAppAdmin.Shared.Dialogs;

namespace WhatsAppAdmin.Pages
{
    public partial class Index : IDisposable
    {
        [Inject] private ImportStateService ImportState { get; set; } = default!;
        [Inject] private WhapiService WhapiService { get; set; } = default!;
        [Inject] private OverlayService OverlayService { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        private List<WhatsAppGroup> _whatsAppGroups = [];

        protected override async Task OnInitializedAsync()
        {
            ImportState.OnChange += OnImportStateChanged;
            if (ImportState.HasGroups)
            {
                _whatsAppGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
            }
            await RefreshGroupsFromWhatsAppAsync();
        }
        private static List<WhatsAppGroup> ConvertImportedToWhatsAppGroups(IEnumerable<WhatsAppGroup> imported)
        {
            var outList = new List<WhatsAppGroup>();
            foreach (var ig in imported)
            {
                var wg = new WhatsAppGroup
                {
                    Id = ig.Id ?? Guid.NewGuid().ToString(),
                    Name = ig.Name ?? string.Empty,
                    Users = [.. ig.Users.Select(u => new User
                    {
                        Id = u.Id == Guid.Empty ? Guid.NewGuid() : u.Id,
                        Name = u.Name ?? string.Empty,
                        Rating = 0,
                        Notes = string.Empty,
                        PhoneNumber = u.PhoneNumber ?? string.Empty
                    })]
                };
                outList.Add(wg);
            }
            return outList;
        }

        private async Task RefreshGroupsFromWhatsAppAsync(bool isAfterDeleteAllUsers = false)
        {
            await OverlayService.RunAsync(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    // 1️⃣ Load contacts.json
                    var contactsFile = Path.Combine(AppContext.BaseDirectory, "contacts.json");
                    var contacts = new List<User>();

                    if (File.Exists(contactsFile))
                    {
                        var json = await File.ReadAllTextAsync(contactsFile);
                        contacts = System.Text.Json.JsonSerializer.Deserialize<List<User>>(json) ?? [];
                    }

                    // 2️⃣ Convert contacts to a dictionary for fast lookup
                    var phoneToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var c in contacts.Where(c => !string.IsNullOrWhiteSpace(c.PhoneNumber)))
                    {
                        var key = MainLayout.NormalizePhone(c.PhoneNumber);

                        // Add or update (last one wins if duplicates exist)
                        phoneToName[key] = c.Name;
                    }

                    // 3️⃣ Fetch groups from WhatsApp
                    var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();

                    // 4️⃣ Convert and enrich with names from contacts.json
                    _whatsAppGroups = [.. ConvertWhatsAppApiGroups(groupsFromWhatsApp, isAfterDeleteAllUsers)
                        .Select(g =>
                        {
                            g.Users = [.. g.Users.Select(u =>
                            {
                                var normalizedPhone = MainLayout.NormalizePhone(u.PhoneNumber);
                                if (phoneToName.TryGetValue(normalizedPhone, out var mappedName))
                                    u.Name = mappedName; // replace name from contacts.json

                                return u;
                            })];
                            return g;
                        })];
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"❌ Failed to refresh groups: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;  
                        config.ShowCloseIcon = true;       
                    });
                }
            }, "Loading");
        }

        private List<WhatsAppGroup> ConvertWhatsAppApiGroups(List<JsonElement> apiGroups, bool isAfterDeleteAllUsers = false)
        {
            var result = new List<WhatsAppGroup>();
            foreach (var g in apiGroups)
            {
                var name = g.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() ?? "Unnamed" : _whatsAppGroups.FirstOrDefault(x => x.Id == g.GetProperty("id").GetString())?.Name ?? "Unnamed";
                var id = g.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                var users = new List<User>();
                if (g.TryGetProperty("participants", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0)
                {
                    static string? GetStringProp(in JsonElement el, string name) => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                    foreach (var user in p.EnumerateArray())
                    {
                        string phone = string.Empty;
                        string rank = string.Empty;
                        string username = string.Empty;

                        if (user.ValueKind == JsonValueKind.String)
                        {
                            phone = user.GetString() ?? string.Empty;
                        }
                        else if (user.ValueKind == JsonValueKind.Object)
                        {
                            phone = GetStringProp(user, "id") ?? GetStringProp(user, "phone") ?? string.Empty;
                            rank = GetStringProp(user, "rank") ?? string.Empty;
                            username = GetStringProp(user, "username") ?? string.Empty;
                        }

                        if (string.IsNullOrWhiteSpace(phone))
                            continue;

                        users.Add(new User
                        {
                            Name = username,
                            PhoneNumber = phone,
                            Rank = rank
                        });
                    }
                }
                else if (!isAfterDeleteAllUsers)
                {
                    var existingGroup = _whatsAppGroups.FirstOrDefault(x => x.Id == id || x.Name == name);
                    if (existingGroup != null)
                        users = existingGroup.Users;
                }
                result.Add(new WhatsAppGroup { Id = id, Name = name, Users = users });
            }
            return result;
        }

        private void OnImportStateChanged()
        {
            InvokeAsync(async () =>
            {
                await OverlayService.RunAsync(async () =>
                {
                    if (ImportState.HasGroups)
                    {
                        var importedGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
                        _whatsAppGroups = await MergeImportedWithWhatsAppAsync(importedGroups);
                    }
                    else
                    {
                        await RefreshGroupsFromWhatsAppAsync();
                    }
                    StateHasChanged();
                }, "Importing");
            });
        }

        private async Task<List<WhatsAppGroup>> MergeImportedWithWhatsAppAsync(IEnumerable<WhatsAppGroup> imported)
        {
            var existingApiGroups = await WhapiService.GetAllGroupsAsync();
            var existingGroups = ConvertWhatsAppApiGroups(existingApiGroups);

            var result = new List<WhatsAppGroup>();

            // --- Loop through imported groups ---
            foreach (var importedGroup in imported)
            {
                var match = existingGroups.FirstOrDefault(
                    g => string.Equals(g.Name, importedGroup.Name, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    // ✅ New group — mark as new and add
                    importedGroup.IsNew = true;
                    result.Add(importedGroup);
                }
                else
                {
                    // ✅ Existing — compare and merge users
                    var normalizedExisting = match.Users
                        .Select(u => MainLayout.NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    var normalizedImported = importedGroup.Users
                        .Select(u => MainLayout.NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    var toAdd = importedGroup.Users
                        .Where(u => !normalizedExisting.Contains(MainLayout.NormalizePhone(u.PhoneNumber)))
                        .ToList();

                    var toRemove = match.Users
                        .Where(u => !normalizedImported.Contains(MainLayout.NormalizePhone(u.PhoneNumber)))
                        .ToList();

                    match.ToAdd = toAdd;
                    match.ToRemove = toRemove;

                    result.Add(match);
                }
            }
            var untouchedGroups = existingGroups
                .Where(g => !imported.Any(i => string.Equals(i.Name, g.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            result.AddRange(untouchedGroups);

            return result;
        }

        private static string GetGroupStatus(WhatsAppGroup g)
        {
            if (g.IsNew) return "new";
            if (g.ToAdd.Count != 0 || g.ToRemove.Count != 0) return "changed";
            return "unchanged";
        }

        public void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks
            ImportState.OnChange -= OnImportStateChanged;

            // Tell the GC there's no need to call a finalizer for this instance
            GC.SuppressFinalize(this);
        }

        private async Task SyncGroupsOnWhatsApp()
        {
            await OverlayService.RunAsync(async () =>
            {
                StateHasChanged();

                try
                {
                    foreach (var group in _whatsAppGroups)
                    {
                        if (group.IsNew)
                        {
                            // create group
                            var phones = group.Users.Select(u => u.PhoneNumber).ToList();
                            group.Id = await WhapiService.CreateGroupAsync(group.Name, phones);
                            Snackbar.Add($"✅ Created new group '{group.Name}'", Severity.Success);
                        }
                        else
                        {
                            // update group
                            if (group.ToAdd.Count != 0)
                            {
                                await WhapiService.AddParticipantsAsync(group.Id, group.ToAdd.Select(u => u.PhoneNumber));
                                Snackbar.Add($"✅ Added {group.ToAdd.Count} users to '{group.Name}'", Severity.Success);
                            }

                            if (group.ToRemove.Count != 0)
                            {
                                try
                                {
                                    await WhapiService.RemoveParticipantsAsync(group.Id, group.ToRemove.Select(u => u.PhoneNumber));
                                }
                                catch (Exception ex)
                                {
                                    Snackbar.Add(ex.Message, Severity.Error, config =>
                                    {
                                        config.RequireInteraction = true;
                                        config.ShowCloseIcon = true;
                                    });
                                    break;
                                }
                                Snackbar.Add($"⚠️ Removed {group.ToRemove.Count} users from '{group.Name}'", Severity.Error);
                            }
                        }
                    }

                    Snackbar.Add("✅ Sync complete.", Severity.Success);
                    await RefreshGroupsFromWhatsAppAsync();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"❌ Error during sync: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                }
                finally
                {
                    StateHasChanged();
                }
            }, "Syncing groups on Whatsapp");
        }

        private async Task DeleteAllGroupsFromWhatsApp()
        {
            await OverlayService.RunAsync(async () =>
            {
                StateHasChanged();

                try
                {
                    int total = _whatsAppGroups.Count;
                    int current = 0;

                    foreach (var group in _whatsAppGroups)
                    {
                        current++;

                        Snackbar.Add($"Deleting group {current}/{total}: {group.Name}", Severity.Error);
                        StateHasChanged();

                        try
                        {
                            await WhapiService.SafeDeleteGroupAsync(group.Id);

                            Snackbar.Add($"✅ Deleted '{group.Name}'", Severity.Error);
                        }
                        catch (Exception ex)
                        {
                            Snackbar.Add($"❌ Failed to delete '{group.Name}': {ex.Message}", Severity.Error, config =>
                            {
                                config.RequireInteraction = true;
                                config.ShowCloseIcon = true;
                            });
                        }

                        await Task.Delay(300); // slight delay to avoid hitting rate limits
                    }

                    Snackbar.Add("All groups processed.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"❌ Fatal error: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                }
                finally
                {
                    StateHasChanged();
                }
            }, "Deleting all groups on Whatsapp");
        }

        private void OpenBroadcastDialog(string? id = null, string? name = null)
        {
            var title = string.IsNullOrWhiteSpace(id) ? "Broadcast to all groups" : $"Send message to {name}";
            var parameters = new DialogParameters();
            if(string.IsNullOrWhiteSpace(id))
            {
                parameters["ToAllGroups"] = true;
            }
            else
            {
                parameters["ToAllGroups"] = false;
                parameters["Id"] = id;
            }
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            DialogService.ShowAsync<BroadcastDialog>(title, parameters, options);
        }

        private async void OpenRenameGroupDialog(string id, string oldName, bool isLocal = false)
        {
            var parameters = new DialogParameters
            {
                ["GroupId"] = id,
                ["OldName"] = oldName,
                ["IsLocal"] = isLocal
            };

            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true
            };

            var dialog = await DialogService.ShowAsync<RenameGroupDialog>("Rename Group", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled)
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }

        private async Task OpenAddUserDialog(string groupName, bool isLocal = false)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = groupName,
                ["IsLocal"] = isLocal
            };

            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true
            };

            var dialog = await DialogService.ShowAsync<AddUserDialog>($"Add User to {groupName}", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled)
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }

        private async Task OpenDeleteGroupDialog(string groupId, string groupName, bool isLocal = false)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = groupName,
                ["GroupId"] = groupId,
                ["IsLocal"] = isLocal
            };

            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true
            };

            var dialog = await DialogService.ShowAsync<DeleteGroupDialog>("Delete All Users", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled)
            {
                // Refresh after deletion
                await RefreshGroupsFromWhatsAppAsync(true);
                StateHasChanged();
            }
        }

        private async Task OpenDeleteUserDialog(string phoneNumber, string groupId, string groupName, bool isLocal = false)
        {
            var parameters = new DialogParameters
            {
                ["PhoneNumber"] = phoneNumber,
                ["GroupName"] = groupName,
                ["GroupId"] = groupId,
                ["IsLocal"] = isLocal
            };

            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true
            };

            var dialog = await DialogService.ShowAsync<DeleteUserDialog>("Delete User", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled)
            {
                // Refresh user list after deletion
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }
    }
}
