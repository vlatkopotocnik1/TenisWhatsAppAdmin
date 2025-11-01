using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Services;
using WhatsAppAdmin.Shared;

namespace WhatsAppAdmin.Pages
{
    public partial class Index : IDisposable
    {
        [Inject] private ImportStateService ImportState { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] private WhapiService WhapiService { get; set; } = default!;

        private List<WhatsAppGroup> _whatsAppGroups = new();
        private bool _loading = true, _isCreating, _isDeleting;

        private bool _contextMenuVisible, _contextUserMenuVisible;
        private string _contextMenuX = "0px", _contextMenuY = "0px";
        private string _contextUserMenuX = "0px", _contextUserMenuY = "0px";
        private string _contextGroupName = String.Empty, _contextUserName = String.Empty;

        // Modal system
        private bool _modalVisible;
        private string _modalTitle = "";
        private string? _modalBodyText;
        private List<PromptDialog.GenericField>? _modalFields;
        private List<PromptDialog.GenericButton>? _modalButtons;

        protected override async Task OnInitializedAsync()
        {
            ImportState.OnChange += OnImportStateChanged;
            if (ImportState.HasGroups)
            {
                _whatsAppGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
            }
            await RefreshGroupsFromWhatsAppAsync();
            GlobalEvents.OnHideContextMenu += HideContextMenu;
            _loading = false;
        }
        private List<WhatsAppGroup> ConvertImportedToWhatsAppGroups(IEnumerable<WhatsAppGroup> imported)
        {
            var outList = new List<WhatsAppGroup>();
            foreach (var ig in imported)
            {
                var wg = new WhatsAppGroup
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
                };
                outList.Add(wg);
            }
            return outList;
        }

        private async Task RefreshGroupsFromWhatsAppAsync()
        {
            try
            {
                var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();
                _whatsAppGroups = ConvertWhatsAppApiGroups(groupsFromWhatsApp);
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Failed to refresh groups: {ex.Message}", "error");
            }
        }

        private List<WhatsAppGroup> ConvertWhatsAppApiGroups(List<JsonElement> apiGroups)
        {
            var result = new List<WhatsAppGroup>();
            foreach (var g in apiGroups)
            {
                var name = g.GetProperty("name").GetString() ?? "Unnamed";
                var id = g.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                var users = new List<User>();
                if (g.TryGetProperty("participants", out var p) && p.ValueKind == JsonValueKind.Array)
                {
                    foreach (var user in p.EnumerateArray())
                    {
                        string? phone = null;

                        if (user.ValueKind == JsonValueKind.String)
                        {
                            phone = user.GetString();
                        }
                        else if (user.ValueKind == JsonValueKind.Object &&
                                 user.TryGetProperty("id", out var idEl) &&
                                 idEl.ValueKind == JsonValueKind.String)
                        {
                            phone = idEl.GetString();
                        }

                        if (!string.IsNullOrWhiteSpace(phone))
                        {
                            users.Add(new User
                            {
                                Id = Guid.NewGuid(),
                                Name = phone,
                                PhoneNumber = phone
                            });
                        }
                    }
                }
                result.Add(new WhatsAppGroup { Id = id, Name = name, Users = users });
            }
            return result;
        }

        private void OnImportStateChanged()
        {
            InvokeAsync(async () =>
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
                        .Select(u => NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    var normalizedImported = importedGroup.Users
                        .Select(u => NormalizePhone(u.PhoneNumber))
                        .ToHashSet();

                    var toAdd = importedGroup.Users
                        .Where(u => !normalizedExisting.Contains(NormalizePhone(u.PhoneNumber)))
                        .ToList();

                    var toRemove = match.Users
                        .Where(u => !normalizedImported.Contains(NormalizePhone(u.PhoneNumber)))
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

        private string GetGroupStatus(WhatsAppGroup g)
        {
            if (g.IsNew) return "new";
            if (g.ToAdd.Any() || g.ToRemove.Any()) return "changed";
            return "unchanged";
        }

        private static string NormalizePhone(string phone) =>
            phone.Replace("+", "").Replace("@c.us", "").Trim();

        private void HideContextMenu()
        {
            _contextMenuVisible = _contextUserMenuVisible = false;
            StateHasChanged();
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
            _contextMenuVisible = false;
            ShowModal(
                "Rename group",
                null,
                new() { new() { Name = "newName", Placeholder = "New group name", Value = groupName } },
                new()
                {
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Rename", CssClass = "btn btn-primary", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async fields =>
                        {
                            var newName = fields?.FirstOrDefault(f => f.Name=="newName")?.Value;
                            if (!string.IsNullOrWhiteSpace(newName))
                                await RenameGroup(groupName, newName);
                        })
                    }
                });
        }

        private async Task RenameGroup(string oldName, string newName)
        {
            var group = _whatsAppGroups.FirstOrDefault(g => g.Name == oldName);
            if (group == null) return;

            // If group is only local, just rename in memory
            if (group.IsNew)
            {
                group.Name = newName;
                await JS.InvokeVoidAsync("showToast", $"✅ Group renamed to {newName}", "info");
                return;
            }

            await WhapiService.UpdateGroupAsync(group.Id, newName);
            group.Name = newName;          
            await JS.InvokeVoidAsync("showToast", $"✅ Group renamed to {newName}", "success");
            await RefreshGroupsFromWhatsAppAsync();
        }

        // ➕ Add User
        private void ConfirmAddUser(string groupName)
        {
            _contextMenuVisible = false;
            ShowModal(
                "Add user",
                null,
                new() { new() { Name = "phone", Placeholder = "Phone" } },
                new()
                {
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Add", CssClass = "btn btn-primary", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async fields =>
                        {
                            var phone = fields?.FirstOrDefault(f => f.Name=="phone")?.Value;
                            if (!string.IsNullOrWhiteSpace(phone))
                                await AddUserToGroup(groupName, phone);
                        })
                    }
                });
        }

        private async Task AddUserToGroup(string groupName, string phone)
        {
            var group = _whatsAppGroups.FirstOrDefault(g => g.Name == groupName);
            if (group == null) return;

            // Create user object
            var newUser = new User
            {
                Name = phone, 
                PhoneNumber = phone
            };

            // If group is new (local only), just update the model
            if (group.IsNew)
            {
                group.Users.Add(newUser);
                await JS.InvokeVoidAsync("showToast", $"✅ Added {phone} to {groupName} (local only)", "info");
                return;
            }

            await WhapiService.AddUserToGroupAsync(groupName, phone);
            await JS.InvokeVoidAsync("showToast", $"✅ User {phone} added to {groupName}", "success");
            await RefreshGroupsFromWhatsAppAsync();
        }

        // 🗑️ Delete Group
        private void ConfirmDeleteGroup(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            _contextMenuVisible = false;
            ShowModal(
                "Delete WhatsApp Group",
                $"Are you sure you want to delete <strong>{groupName}</strong>?",
                null,
                new()
                {
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Delete", CssClass = "btn btn-danger", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async _ => await DeleteGroup(groupName)) }
                });
        }

        private async Task DeleteGroup(string groupName)
        {
            var group = _whatsAppGroups.FirstOrDefault(x => x.Name == groupName);
            if (group == null) return;

            if (group.IsNew)
            {
                _whatsAppGroups.Remove(group);
                await JS.InvokeVoidAsync("showToast", $"✅ Group '{groupName}' removed", "info");
                return;
            }

            await WhapiService.SafeDeleteGroupAsync(group.Id);
            await RefreshGroupsFromWhatsAppAsync();
            await JS.InvokeVoidAsync("showToast", $"✅ Deleted '{groupName}'", "success");
        }

        // 🗑️ Delete User
        private void ConfirmDeleteUser(string? userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return;
            _contextUserMenuVisible = false;
            ShowModal(
                "Delete User",
                $"Are you sure you want to delete <strong>{userName}</strong>?",
                null,
                new()
                {
                    new() { Text = "Cancel", CssClass = "btn btn-secondary", CloseOnClick = true },
                    new() { Text = "Delete", CssClass = "btn btn-danger", OnClick = EventCallback.Factory.Create<List<PromptDialog.GenericField>?>(this, async _ => await DeleteUser(userName)) }
                });
        }

        private async Task DeleteUser(string userName)
        {
            var group = _whatsAppGroups.FirstOrDefault(g => g.Users.Any(u => u.Name == userName));
            if (group == null) return;

            // Check if user is only in "ToAdd" (not yet on WhatsApp)
            var userInToAdd = group.ToAdd.FirstOrDefault(u => u.Name == userName);
            if (userInToAdd != null)
            {
                group.ToAdd.Remove(userInToAdd);
                await JS.InvokeVoidAsync("showToast", $"✅ User {userName} removed", "info");
                return;
            }

            var user = group.Users.FirstOrDefault(u => u.Name == userName);
            if (user == null) return;

            // If group not yet created on WhatsApp, just remove locally
            if (group.IsNew)
            {
                group.Users.Remove(user);
                await JS.InvokeVoidAsync("showToast", $"✅ User {userName} removed", "info");
                return;
            }

            await WhapiService.RemoveParticipantsAsync(group.Id, [user.PhoneNumber]);
            group.Users.Remove(user);
            await JS.InvokeVoidAsync("showToast", $"✅ User {userName} removed", "success");
        }

        public void Dispose()
        {
            ImportState.OnChange -= OnImportStateChanged;
            GlobalEvents.OnHideContextMenu -= HideContextMenu;
        }

        private async Task SyncGroupsOnWhatsApp()
        {
            _isCreating = true;

            await JS.InvokeVoidAsync("showToast", "Starting group sync…", "info");
            StateHasChanged();

            try
            {
                foreach (var group in _whatsAppGroups)
                {
                    var isChanged = false;
                    if (group.IsNew)
                    {
                        // create group
                        var phones = group.Users.Select(u => u.PhoneNumber).ToList();
                        group.Id = await WhapiService.CreateGroupAsync(group.Name, phones);
                        await JS.InvokeVoidAsync("showToast", $"✅ Created new group '{group.Name}' → {group.Id}", "success");
                        isChanged = true;
                    }
                    else
                    {
                        // update group
                        if (group.ToAdd.Any())
                        {
                            await WhapiService.AddParticipantsAsync(group.Id, group.ToAdd.Select(u => u.PhoneNumber));
                            await JS.InvokeVoidAsync("showToast", $"✅ Added {group.ToAdd.Count} users to '{group.Name}'", "success");
                            isChanged = true;
                        }

                        if (group.ToRemove.Any())
                        {
                            await WhapiService.RemoveParticipantsAsync(group.Id, group.ToRemove.Select(u => u.PhoneNumber));
                            await JS.InvokeVoidAsync("showToast", $"⚠️ Removed {group.ToRemove.Count} users from '{group.Name}'", "error");
                            isChanged = true;
                        }
                    }
                    if(isChanged) await Task.Delay(300); // gentle pacing
                }
                await RefreshGroupsFromWhatsAppAsync();
                await JS.InvokeVoidAsync("showToast", "✅ Sync complete.", "success");
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Error during sync: {ex.Message}", "error");
            }
            finally
            {
                _isCreating = false;
                StateHasChanged();
            }
        }

        private async Task DeleteAllGroupsFromWhatsApp()
        {
            _isDeleting = true;
            await JS.InvokeVoidAsync("showToast", "Deleting all WhatsApp groups…", "info");
            StateHasChanged();

            try
            {
                int total = _whatsAppGroups.Count;
                int current = 0;

                foreach (var group in _whatsAppGroups)
                {
                    current++;

                    await JS.InvokeVoidAsync("showToast", $"Deleting group {current}/{total}: {group.Name}", "error");
                    StateHasChanged();

                    try
                    {
                        await WhapiService.SafeDeleteGroupAsync(group.Id);

                        await JS.InvokeVoidAsync("showToast", $"✅ Deleted '{group.Name}'", "error");
                    }
                    catch (Exception ex)
                    {
                        await JS.InvokeVoidAsync("showToast", $"❌ Failed to delete '{group.Name}': {ex.Message}", "error");
                    }

                    await Task.Delay(300); // slight delay to avoid hitting rate limits
                }

                await JS.InvokeVoidAsync("showToast", "All groups processed.", "success");
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Fatal error: {ex.Message}", "error");
            }
            finally
            {
                _isDeleting = false;
                StateHasChanged();
            }
        }

        private void ToggleGroupMenu(MouseEventArgs e, string name)
        {
            if (_contextGroupName == name && _contextMenuVisible)
            {
                _contextMenuVisible = false;
            }
            else
            {
                _contextMenuVisible = true;
                _contextUserMenuVisible = false;
                _contextMenuX = $"{e.ClientX - 40}px";
                _contextMenuY = $"{e.ClientY + 12}px";
                _contextGroupName = name;
            }
            StateHasChanged();
        }

        private void ToggleUserMenu(MouseEventArgs e, string name)
        {
            if (_contextUserName == name && _contextUserMenuVisible)
            {
                _contextUserMenuVisible = false;
            }
            else
            {
                _contextUserMenuVisible = true;
                _contextMenuVisible = false;
                _contextUserMenuX = $"{e.ClientX - 40}px";
                _contextUserMenuY = $"{e.ClientY + 12}px";
                _contextUserName = name;
            }
            StateHasChanged();
        }

    }
}
