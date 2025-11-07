using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Services;
using WhatsAppAdmin.Shared;

namespace WhatsAppAdmin.Pages
{
    public partial class Index : IDisposable
    {
        [Inject] private ImportStateService ImportState { get; set; } = default!;
        [Inject] private WhapiService WhapiService { get; set; } = default!;
        [Inject] private OverlayService OverlayService { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        private List<WhatsAppGroup> _whatsAppGroups = [];

        // Modal system
        private bool _modalVisible;
        private string _modalTitle = "";
        private string? _modalBodyText;
        private List<PromptDialog.GenericField>? _modalFields;
        private List<PromptDialog.GenericButton>? _modalButtons;
        private record MenuPosition(double X, double Y);

        protected override async Task OnInitializedAsync()
        {
            ImportState.OnChange += OnImportStateChanged;
            if (ImportState.HasGroups)
            {
                _whatsAppGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
            }
            await RefreshGroupsFromWhatsAppAsync();
            GlobalEvents.OnHideContextMenu += HideContextMenu;
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
                        contacts = System.Text.Json.JsonSerializer.Deserialize<List<User>>(json) ?? new();
                    }

                    // 2️⃣ Convert contacts to a dictionary for fast lookup
                    var phoneToName = contacts
                        .Where(c => !string.IsNullOrWhiteSpace(c.PhoneNumber))
                        .ToDictionary(
                            c => MainLayout.NormalizePhone(c.PhoneNumber),
                            c => c.Name,
                            StringComparer.OrdinalIgnoreCase
                        );

                    // 3️⃣ Fetch groups from WhatsApp
                    var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();

                    // 4️⃣ Convert and enrich with names from contacts.json
                    _whatsAppGroups = ConvertWhatsAppApiGroups(groupsFromWhatsApp, isAfterDeleteAllUsers)
                        .Select(g =>
                        {
                            g.Users = g.Users.Select(u =>
                            {
                                var normalizedPhone = MainLayout.NormalizePhone(u.PhoneNumber);
                                if (phoneToName.TryGetValue(normalizedPhone, out var mappedName))
                                    u.Name = mappedName; // replace name from contacts.json

                                return u;
                            }).ToList();
                            return g;
                        }).ToList();
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

        private void HideContextMenu()
        {
            InvokeAsync(StateHasChanged);
        }

        // 🔹 Generic modal helper
        private void ShowModal(string title, string? body, List<PromptDialog.GenericField>? fields, List<PromptDialog.GenericButton> buttons)
        {
            _modalTitle = title;
            _modalBodyText = body;
            _modalFields = fields;
            _modalButtons = buttons;
            _modalVisible = true;
        }

        // 📝 Rename Group
        private void ConfirmRenameGroup(string groupName)
        {
            ShowModal(
                "Rename group",
                null,
                [new() { Name = "newName", Placeholder = "New group name", Value = groupName }],
                [
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Rename", CssClass = "btn btn-primary", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async fields =>
                        {
                            var newName = fields?.FirstOrDefault(f => f.Name=="newName")?.Value;
                            if (!string.IsNullOrWhiteSpace(newName))
                                await RenameGroup(groupName, newName);
                        })
                    }
                ]);
        }

        private async Task RenameGroup(string oldName, string newName)
        {

            await OverlayService.RunAsync(async () =>
            {
                var group = _whatsAppGroups.FirstOrDefault(g => g.Name == oldName);
                if (group == null) return;

                // If group is only local, just rename in memory
                if (group.IsNew)
                {
                    group.Name = newName;
                    Snackbar.Add($"✅ Group renamed to {newName} (local only)", Severity.Info);
                    return;
                }

                try
                {
                    await WhapiService.UpdateGroupAsync(group.Id, newName);
                }
                catch(Exception ex)
                {
                    Snackbar.Add($"❌ Failed to rename group: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                }
                group.Name = newName;

                Snackbar.Add($"✅ Group renamed to {newName}", Severity.Success);
                await RefreshGroupsFromWhatsAppAsync();
            }, $"Renaming");
        }

        // ➕ Add User
        private void ConfirmAddUser(string groupName)
        {
            ShowModal(
                "Add user",
                null,
                [new() { Name = "phone", Placeholder = "Phone" }],
                [
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Add", CssClass = "btn btn-primary", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async fields =>
                        {
                            var phone = fields?.FirstOrDefault(f => f.Name=="phone")?.Value;
                            if (!string.IsNullOrWhiteSpace(phone))
                                await AddUserToGroup(groupName, phone);
                        })
                    }
                ]);
        }

        private async Task AddUserToGroup(string groupName, string phone)
        {
            await OverlayService.RunAsync(async () =>
            {
                var group = _whatsAppGroups.FirstOrDefault(g => g.Name == groupName);
                if (group == null) return;

                var ifExistingUser = group.Users.FirstOrDefault(u => string.Equals(u.PhoneNumber, MainLayout.NormalizePhone(phone).Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

                if (ifExistingUser != null)
                {
                    Snackbar.Add($"⚠️ User {phone} is already in {groupName}", Severity.Warning);
                    return;
                }

                // Create user object
                var newUser = new User
                {
                    PhoneNumber = phone
                };

                // If group is new (local only), just update the model
                if (group.IsNew)
                {
                    group.Users.Add(newUser);
                    Snackbar.Add($"✅ Added {phone} to {groupName} (local only)", Severity.Info);
                    return;
                }

                try
                {
                    await WhapiService.AddUserToGroupAsync(groupName, phone);
                }

                catch(Exception ex)
                {
                    Snackbar.Add($"❌ Failed to add user to group: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                }
                Snackbar.Add($"✅ User {phone} added to {groupName}", Severity.Success);
                await RefreshGroupsFromWhatsAppAsync();
            }, "Adding user");
        }

        // 🗑️ Delete Group
        private void ConfirmDeleteAllUsersFromGroup(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            ShowModal(
                "Delete all users from WhatsApp group",
                $"Are you sure you want to delete all users from <strong>{groupName}</strong>?",
                null,
                [
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Delete", CssClass = "btn btn-danger", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async _ => await DeleteAllUsersFromGroup(groupName)) }
                ]);
        }

        private async Task DeleteAllUsersFromGroup(string groupName)
        {
            await OverlayService.RunAsync(async () =>
            {
                var group = _whatsAppGroups.FirstOrDefault(x => x.Name == groupName);
                if (group == null) return;

                if (group.IsNew)
                {
                    _whatsAppGroups.Remove(group);
                    Snackbar.Add($"✅ Users from group '{groupName}' removed  (local only)", Severity.Info);
                    return;
                }

                try
                {
                    await WhapiService.SafeDeleteGroupAsync(group.Id);
                }
                catch (InvalidOperationException ex)
                {
                    Snackbar.Add(ex.Message, Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"❌ Failed to delete all users from group: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                    return;
                }
                Snackbar.Add($"✅ Users deleted from '{groupName}'", Severity.Error);
                await RefreshGroupsFromWhatsAppAsync(true);
            }, $"Deleting users");
        }

        // 🗑️ Delete User
        private void ConfirmDeleteUser(string phoneNumber, string groupName)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return;
            ShowModal(
                "Delete User",
                $"Are you sure you want to delete <strong>{phoneNumber}</strong> from <strong>{groupName}</strong>?",
                null,
                [
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Delete", CssClass = "btn btn-danger", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async _ => await DeleteUser(phoneNumber)) }
                ]);
        }

        private async Task DeleteUser(string phoneNumber)
        {
            await OverlayService.RunAsync(async () =>
            {
                var group = _whatsAppGroups.FirstOrDefault(g => g.Users.Any(u => u.PhoneNumber == phoneNumber));
                if (group == null) return;

                // Check if user is only in "ToAdd" (not yet on WhatsApp)
                var userInToAdd = group.ToAdd.FirstOrDefault(u => u.PhoneNumber == phoneNumber);
                if (userInToAdd != null)
                {
                    group.ToAdd.Remove(userInToAdd);
                    Snackbar.Add($"✅ User {phoneNumber} removed (local only)", Severity.Info);
                    return;
                }

                var user = group.Users.FirstOrDefault(u => u.PhoneNumber == phoneNumber);
                if (user == null) return;

                // If group not yet created on WhatsApp, just remove locally
                if (group.IsNew)
                {
                    group.Users.Remove(user);
                    Snackbar.Add($"✅ User {phoneNumber} removed (local only)", Severity.Info);
                    return;
                }

                try
                {
                    await WhapiService.RemoveParticipantsAsync(group.Id, [user.PhoneNumber]);
                }
                catch (InvalidOperationException ex)
                {
                    Snackbar.Add(ex.Message, Severity.Error);
                    return;
                }
                catch(Exception ex)
                {
                    Snackbar.Add($"❌ Failed to delete user from group: {ex.Message}", Severity.Error, config =>
                    {
                        config.RequireInteraction = true;
                        config.ShowCloseIcon = true;
                    });
                }
                group.Users.Remove(user);
                Snackbar.Add($"✅ User {phoneNumber} removed", Severity.Error);
            }, "Deleting user");
        }

        public void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks
            ImportState.OnChange -= OnImportStateChanged;
            GlobalEvents.OnHideContextMenu -= HideContextMenu;

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

        private void OpenBroadcastAllDialog()
        {
            var parameters = new DialogParameters { ["ToAllGroups"] = true };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            DialogService.ShowAsync<BroadcastDialog>("Broadcast to all groups", parameters, options);
        }

        private void OpenBroadcastSingleDialog(string id, string name)
        {
            var parameters = new DialogParameters { ["ToAllGroups"] = false, ["Id"] = id };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            DialogService.ShowAsync<BroadcastDialog>($"Send message to {name}", parameters, options);
        }
    }
}
