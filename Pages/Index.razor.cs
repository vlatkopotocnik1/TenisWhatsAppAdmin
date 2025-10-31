using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Services;

namespace WhatsAppAdmin.Pages
{
    public partial class Index
    {
        private List<WhatsAppGroup> _whatsAppGroups = new();
        private bool _loading = true;
        private bool _isCreating = false;
        private bool _isDeleting = false;

        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            ImportState.OnChange += ImportState_OnChange;

            if (ImportState.HasGroups)
            {
                _whatsAppGroups = ConvertImportedToWhatsAppGroups(ImportState.Groups);
            }
            //else
            //{
            //    var groupsFromWhatsApp = await WhapiService.GetAllGroupsAsync();
            //    _whatsAppGroups = ConvertWhatsAppApiGroups(groupsFromWhatsApp);

            //    // 👇 Replace phone numbers with names
            //    foreach (var group in _whatsAppGroups)
            //    {
            //        foreach (var user in group.Users)
            //        {
            //            var name = await WhapiService.GetContactNameAsync(user.PhoneNumber);
            //            if (!string.IsNullOrWhiteSpace(name))
            //            {
            //                user.Name = name;
            //            }
            //        }
            //    }
            //}
            await RefreshGroupsFromWhatsAppAsync();
            GlobalEvents.OnHideContextMenu += HideContextMenu;
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

            await JS.InvokeVoidAsync("showToast", "Starting group sync…", "info");
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
                        await JS.InvokeVoidAsync("showToast", $"✅ Created new group '{group.Name}' → {group.Id}", "success");
                    }
                    else
                    {
                        // update group
                        if (group.ToAdd.Any())
                        {
                            await WhapiService.AddParticipantsAsync(group.Id, group.ToAdd.Select(u => u.PhoneNumber));
                            await JS.InvokeVoidAsync("showToast", $"✅ Added {group.ToAdd.Count} users to '{group.Name}'", "success");
                        }

                        if (group.ToRemove.Any())
                        {
                            await WhapiService.RemoveParticipantsAsync(group.Id, group.ToRemove.Select(u => u.PhoneNumber));
                            await JS.InvokeVoidAsync("showToast", $"⚠️ Removed {group.ToRemove.Count} users from '{group.Name}'", "error");
                        }
                    }

                    await Task.Delay(300); // gentle pacing
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


        public void Dispose()
        {
            ImportState.OnChange -= ImportState_OnChange;
            GlobalEvents.OnHideContextMenu -= HideContextMenu;
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
        private bool _showDeleteConfirm;
        private string? _pendingDeleteGroupName;


        private string? _deletingUserName = null;
        private bool _showDeleteUserConfirm;
        private string? _pendingDeleteUserName;

        private void ShowDeleteConfirm(string groupName)
        {
            _pendingDeleteGroupName = groupName;
            _showDeleteConfirm = true;
        }

        private void CancelDeleteGroup()
        {
            _showDeleteConfirm = false;
            _pendingDeleteGroupName = null;
        }

        private void CancelDeleteUser()
        {
            _showDeleteUserConfirm = false;
            _pendingDeleteUserName = null;
        }

        private async Task ConfirmDeleteGroup()
        {
            _showDeleteConfirm = false;
            if (!string.IsNullOrEmpty(_pendingDeleteGroupName))
                await DeleteGroup(_pendingDeleteGroupName);
            _pendingDeleteGroupName = null;
        }

        private async Task ConfirmDeleteUser()
        {
            _showDeleteUserConfirm = false;
            if (!string.IsNullOrEmpty(_pendingDeleteUserName))
                await DeleteUser(_pendingDeleteUserName);
            _pendingDeleteUserName = null;
        }
        private async Task DeleteGroup(string groupName)
        {
            try
            {
                _deletingGroupName = groupName;
                StateHasChanged();

                var groupToRemove = _whatsAppGroups.FirstOrDefault(g => g.Name == groupName);
                if (groupToRemove == null)
                    return;

                await JS.InvokeVoidAsync("showToast", $"Deleting group {groupToRemove.Name}.", "error");
                if (!string.IsNullOrEmpty(groupToRemove.Id))
                    await WhapiService.SafeDeleteGroupAsync(groupToRemove.Id);

                _whatsAppGroups.Remove(groupToRemove);
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Error deleting group '{groupName}': {ex.Message}", "error");
            }
            finally
            {
                await JS.InvokeVoidAsync("showToast", $"✅ Group deleted.", "error");
                _deletingGroupName = null;
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            }
        }

        private async Task DeleteUser(string userName)
        {
            try
            {
                StateHasChanged();

                // Find the group that contains this user
                var group = _whatsAppGroups.FirstOrDefault(g => g.Users.Any(u => u.Name == userName || u.PhoneNumber == userName));
                if (group == null)
                {
                    await JS.InvokeVoidAsync("showToast", $"⚠️ Could not find a group for user {userName}.", "error");
                    return;
                }

                var user = group.Users.FirstOrDefault(u => u.Name == userName || u.PhoneNumber == userName);
                if (user == null)
                {
                    await JS.InvokeVoidAsync("showToast", $"⚠️ User {userName} not found in group {group.Name}.", "error");
                    return;
                }

                await JS.InvokeVoidAsync("showToast", $"Removing {user.PhoneNumber} from {group.Name}…", "info");

                if (!string.IsNullOrEmpty(group.Id))
                {
                    await WhapiService.RemoveParticipantsAsync(group.Id, new[] { user.PhoneNumber });
                }

                group.Users.Remove(user);

                await JS.InvokeVoidAsync("showToast", $"✅ User {user.PhoneNumber} removed from {group.Name}.", "success");
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Error removing user '{userName}': {ex.Message}", "error");
            }
            finally
            {
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
                await JS.InvokeVoidAsync("showToast", $"❌ Failed to refresh groups: {ex.Message}", "error");
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

        private bool _contextUserMenuVisible = false;
        private string _contextUserMenuX = "0px";
        private string _contextUserMenuY = "0px";
        private string? _contextUserName;

        private void ShowContextMenu(MouseEventArgs e, string groupName)
        {
            _contextMenuVisible = true;
            _contextUserMenuVisible = false;
            _contextMenuX = $"{e.ClientX}px";
            _contextMenuY = $"{e.ClientY}px";
            _contextGroupName = groupName;

            StateHasChanged();
        }

        private void ShowUserContextMenu(MouseEventArgs e, string userName)
        {
            _contextUserMenuVisible = true;
            _contextMenuVisible = false;
            _contextUserMenuX = $"{e.ClientX}px";
            _contextUserMenuY = $"{e.ClientY}px";
            _contextUserName = userName;

            StateHasChanged();
        }

        private void ConfirmDeleteGroup(string? groupName)
        {
            _contextMenuVisible = false;
            if (string.IsNullOrWhiteSpace(groupName)) return;
            ShowDeleteConfirm(groupName);
        }

        private void ConfirmDeleteUser(string? userName)
        {
            _contextUserMenuVisible = false;
            if (string.IsNullOrWhiteSpace(userName)) return;
            ShowDeleteUserConfirm(userName);
        }

        private void ShowDeleteUserConfirm(string userName)
        {
            _pendingDeleteUserName = userName;
            _showDeleteUserConfirm = true;
        }

        private void HideContextMenu()
        {
            _contextMenuVisible = false;
            _contextUserMenuVisible = false;
            InvokeAsync(StateHasChanged);
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
        private void BeginRenameGroup(string groupName)
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
                await JS.InvokeVoidAsync("showToast", $"Rename failed: {ex.Message}", "error");
            }
            finally
            {

                await JS.InvokeVoidAsync("showToast", $"✅ Group renamed.", "success");
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
                await JS.InvokeVoidAsync("showToast", $"⚠️ Please enter a phone number.", "error");
                return;
            }
            _isAddNewUser = true;
            try
            {
                await WhapiService.AddUserToGroupAsync(_selectedGroupName, _newUserPhone);
                await JS.InvokeVoidAsync("showToast", $"✅ User {_newUserPhone} added to {_selectedGroupName}.", "success");
                await RefreshGroupsFromWhatsAppAsync();
            }
            catch (Exception ex)
            {
                await JS.InvokeVoidAsync("showToast", $"❌ Failed to add user: {ex.Message}", "error");
            }
            finally
            {
                _showAddUserModal = false;
            }
        }
        private async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !_isAddNewUser && !string.IsNullOrWhiteSpace(_newUserPhone))
            {
                await ConfirmAddUser();
            }
        }
    }
}
