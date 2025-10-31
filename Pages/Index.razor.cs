using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WhatsAppAdmin.Models;

namespace WhatsAppAdmin.Pages
{
    public partial class Index
    {
        private List<WhatsAppGroup> _whatsAppGroups = new();
        private bool _loading = true;
        private bool _isCreating = false;
        private string? _statusMessage;
        private bool _isDeleting = false;

        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            ImportState.OnChange += ImportState_OnChange;

            if (ImportState.HasGroups)
            {
                _whatsAppGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
            }
            else
            {
                var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();
                _whatsAppGroups = ConvertWhatsAppApiGroups(groupsFromWhatsApp);

                // 👇 Replace phone numbers with names
                foreach (var group in _whatsAppGroups)
                {
                    foreach (var user in group.Users)
                    {
                        var name = await WhapiService.GetContactNameAsync(user.PhoneNumber);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            user.Name = name;
                        }
                    }
                }
            }
            await RefreshGroupsFromWhatsAppAsync();
            _loading = false;
        }

        private async Task<List<WhatsAppGroup>?> SafeGetServiceGroupsAsync()
        {
            try
            {
                if (Service != null)
                {
                    return await Service.GetWhatsAppGroupsAsync();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private void ImportState_OnChange()
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
                    await ReloadFromService();
                }
                StateHasChanged();
            });
        }

        private async Task ReloadFromService()
        {
            _loading = true;
            StateHasChanged();

            _whatsAppGroups = (await SafeGetServiceGroupsAsync()) ?? new List<WhatsAppGroup>();

            _loading = false;
            StateHasChanged();
        }

        private string GetGroupStatus(WhatsAppGroup group)
        {
            if (group.IsNew)
                return "new";
            if (group.ToAdd.Any() || group.ToRemove.Any())
                return "changed";
            return "unchanged";
        }

        private async Task SyncGroupsOnWhatsApp()
        {
            _isCreating = true;
            _statusMessage = "Starting group sync…";
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
                        _statusMessage += $"\n✅ Created new group '{group.Name}' → {group.Id}";
                    }
                    else
                    {
                        // update group
                        if (group.ToAdd.Any())
                        {
                            await WhapiService.AddParticipantsAsync(group.Id, group.ToAdd.Select(u => u.PhoneNumber));
                            _statusMessage += $"\n✅ Added {group.ToAdd.Count} users to '{group.Name}'";
                        }

                        if (group.ToRemove.Any())
                        {
                            await WhapiService.RemoveParticipantsAsync(group.Id, group.ToRemove.Select(u => u.PhoneNumber));
                            _statusMessage += $"\n⚠️ Removed {group.ToRemove.Count} users from '{group.Name}'";
                        }
                    }

                    await Task.Delay(300); // gentle pacing
                }
                await RefreshGroupsFromWhatsAppAsync();
                _statusMessage += "\n✅ Sync complete.";
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ Error during sync: {ex.Message}";
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
            _statusMessage = "Deleting all WhatsApp groups…";
            StateHasChanged();

            try
            {
                int total = _whatsAppGroups.Count;
                int current = 0;

                foreach (var group in _whatsAppGroups)
                {
                    current++;
                    _statusMessage = $"Deleting group {current}/{total}: {group.Name}";
                    StateHasChanged();

                    try
                    {
                        // Replace this with your Whapi helper (same one you used for Create)
                        await WhapiService.SafeDeleteGroupAsync(group.Id);
                        _statusMessage += $"\n✅ Deleted '{group.Name}'";
                    }
                    catch (Exception ex)
                    {
                        _statusMessage += $"\n❌ Failed to delete '{group.Name}': {ex.Message}";
                    }

                    await Task.Delay(300); // slight delay to avoid hitting rate limits
                }

                _statusMessage += "\nAll groups processed.";
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ Fatal error: {ex.Message}";
            }
            finally
            {
                _isDeleting = false;
                StateHasChanged();
            }
        }


        public void Dispose()
        {
            ImportState.OnChange -= ImportState_OnChange;
        }

        private List<WhatsAppGroup> ConvertWhatsAppApiGroups(List<JsonElement> apiGroups)
        {
            var result = new List<WhatsAppGroup>();

            foreach (var group in apiGroups)
            {
                var name = group.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Unnamed" : "Unnamed";
                var id = group.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

                var users = new List<Users>();
                if (group.TryGetProperty("participants", out var participantsEl) && participantsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in participantsEl.EnumerateArray())
                    {
                        var phone = p.ValueKind == JsonValueKind.String
                            ? p.GetString()
                            : (p.TryGetProperty("id", out var idPart) ? idPart.GetString() : null);

                        if (!string.IsNullOrWhiteSpace(phone))
                        {
                            users.Add(new Users
                            {
                                Id = Guid.NewGuid(),
                                Name = phone, // temporarily, will update below
                                PhoneNumber = phone!,
                                Rating = 0,
                                Notes = ""
                            });
                        }
                    }
                }

                result.Add(new WhatsAppGroup
                {
                    Id = id,
                    Name = name,
                    Users = users
                });
            }

            return result;
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
                    Users = ig.Users.Select(u => new Users
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

        private string? _deletingGroupName = null;

        private async Task DeleteGroup(string groupName)
        {
            try
            {
                bool confirm = await JSRuntime.InvokeAsync<bool>(
                    "confirm",
                    $"Are you sure you want to permanently delete '{groupName}'?"
                );

                if (!confirm)
                    return;

                _deletingGroupName = groupName;
                StateHasChanged();

                var groupToRemove = _whatsAppGroups.FirstOrDefault(g => g.Name == groupName);
                if (groupToRemove == null)
                    return;

                if (!string.IsNullOrEmpty(groupToRemove.Id))
                    await WhapiService.SafeDeleteGroupAsync(groupToRemove.Id);

                _whatsAppGroups.Remove(groupToRemove);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting group '{groupName}': {ex.Message}");
            }
            finally
            {
                _deletingGroupName = null;
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }

        private async Task RefreshGroupsFromWhatsAppAsync()
        {
            try
            {
                var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();
                _whatsAppGroups = ConvertWhatsAppApiGroups(groupsFromWhatsApp);

                // Update display names again
                foreach (var group in _whatsAppGroups)
                {
                    foreach (var user in group.Users)
                    {
                        var name = await WhapiService.GetContactNameAsync(user.PhoneNumber);
                        if (!string.IsNullOrWhiteSpace(name))
                            user.Name = name;
                    }
                }

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to refresh groups: {ex.Message}");
            }
        }

        private static string NormalizePhone(string phone)
        {
            return phone.Replace("+", "").Replace("@c.us", "").Trim();
        }

        private bool _contextMenuVisible = false;
        private string _contextMenuX = "0px";
        private string _contextMenuY = "0px";
        private string? _contextGroupName;

        private void ShowContextMenu(MouseEventArgs e, string groupName)
        {
            _contextMenuVisible = true;
            _contextMenuX = $"{e.ClientX}px";
            _contextMenuY = $"{e.ClientY}px";
            _contextGroupName = groupName;

            StateHasChanged();
        }

        private async Task ConfirmDeleteGroup(string? groupName)
        {
            _contextMenuVisible = false;
            if (string.IsNullOrWhiteSpace(groupName)) return;
            await DeleteGroup(groupName);
        }

        private void HideContextMenu()
        {
            _contextMenuVisible = false;
            StateHasChanged();
        }

        // fields for rename dialog
        private bool _renameDialogVisible = false;
        private string? _renameOriginalGroupName;
        private string _renameNewName = string.Empty;
        private bool _isRenaming = false;

        /// <summary>
        /// Called when user clicks 'Rename Group' in context menu.
        /// Shows rename modal populated with current name.
        /// </summary>
        private void BeginRenameGroup(string? groupName)
        {
            _contextMenuVisible = false;
            if (string.IsNullOrWhiteSpace(groupName))
                return;

            _renameOriginalGroupName = groupName;
            _renameNewName = groupName; // prefill with current name
            _renameDialogVisible = true;
            StateHasChanged();
        }

        private void CloseRenameDialog()
        {
            _renameDialogVisible = false;
            _renameOriginalGroupName = null;
            _renameNewName = string.Empty;
            StateHasChanged();
        }

        private async Task ConfirmRenameGroup()
        {
            if (string.IsNullOrWhiteSpace(_renameOriginalGroupName) || string.IsNullOrWhiteSpace(_renameNewName))
                return;

            try
            {
                _isRenaming = true;
                StateHasChanged();

                // find group object by name
                var group = _whatsAppGroups.FirstOrDefault(g => g.Name == _renameOriginalGroupName);
                if (group == null)
                {
                    // maybe it was an imported-only group (try find by Id if you saved it)
                    CloseRenameDialog();
                    return;
                }

                // call backend to update
                if (!string.IsNullOrWhiteSpace(group.Id))
                {
                    // call WhapiService PUT /groups/{groupId}
                    await WhapiService.UpdateGroupAsync(group.Id, _renameNewName);
                }
                else
                {
                    // It is a local/imported group that doesn't exist on WhatsApp yet; just rename locally
                }

                // update local model immediately
                group.Name = _renameNewName;

                // refresh server-side state to be safe (re-fetch groups)
                await RefreshGroupsFromWhatsAppAsync();

                CloseRenameDialog();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Rename failed: {ex.Message}");
                // optionally show _statusMessage for user
                _statusMessage = $"Rename failed: {ex.Message}";
            }
            finally
            {
                _isRenaming = false;
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }
        // Modal state
        private bool _showAddUserModal = false;
        private bool _isAddNewUser = false;
        private string _selectedGroupName = string.Empty;
        private string _newUserPhone = string.Empty;

        // Show modal
        private void ShowAddUserModal(string groupName)
        {
            _selectedGroupName = groupName;
            _newUserPhone = string.Empty;
            _showAddUserModal = true;
            _contextMenuVisible = false;
        }

        // Close modal
        private void CloseAddUserModal()
        {
            _showAddUserModal = false;
        }

        // Confirm add user
        private async Task ConfirmAddUser()
        {
            if (string.IsNullOrWhiteSpace(_newUserPhone))
            {
                _statusMessage = $"⚠️ Please enter a phone number.";
                return;
            }
            _isAddNewUser = true;
            try
            {
                await WhapiService.AddUserToGroupAsync(_selectedGroupName, _newUserPhone);
                _statusMessage = $"✅ User {_newUserPhone} added to {_selectedGroupName}.";
                await RefreshGroupsFromWhatsAppAsync();
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ Failed to add user: {ex.Message}";
            }
            finally
            {
                _showAddUserModal = false;
            }
        }

    }
}
