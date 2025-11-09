using MudBlazor;
using WhatsAppAdmin.Shared.Dialogs;

namespace WhatsAppAdmin.Helpers
{
    public class DialogManager
    {
        private readonly IDialogService _dialog;

        public DialogManager(IDialogService dialog) => _dialog = dialog;

        // Broadcast dialog
        public async Task OpenBroadcastDialogAsync(string? id = null, string? name = null, Func<Task>? onSuccess = null)
        {
            var title = string.IsNullOrWhiteSpace(id)
                ? "Broadcast to all groups"
                : $"Send message to {name}";

            var parameters = new DialogParameters
            {
                ["ToAllGroups"] = string.IsNullOrWhiteSpace(id),
                ["Id"] = id
            };

            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var dialog = await _dialog.ShowAsync<BroadcastDialog>(title, parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && onSuccess is not null)
                await onSuccess.Invoke();
        }

        // Rename group dialog
        public async Task OpenRenameGroupDialogAsync(string id, string oldName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupId"] = id,
                ["OldName"] = oldName,
                ["IsLocal"] = isLocal
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await _dialog.ShowAsync<RenameGroupDialog>("Rename Group", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && onSuccess is not null)
                await onSuccess.Invoke();
        }

        // Add user dialog
        public async Task OpenAddUserDialogAsync(string groupName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = groupName,
                ["IsLocal"] = isLocal
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await _dialog.ShowAsync<AddUserDialog>($"Add User to {groupName}", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && onSuccess is not null)
                await onSuccess.Invoke();
        }

        // Delete group dialog
        public async Task OpenDeleteGroupDialogAsync(string id, string name, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = name,
                ["GroupId"] = id,
                ["IsLocal"] = isLocal
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await _dialog.ShowAsync<DeleteGroupDialog>("Delete All Users", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && onSuccess is not null)
                await onSuccess.Invoke();
        }

        // Delete user dialog
        public async Task OpenDeleteUserDialogAsync(string phone, string groupId, string groupName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["PhoneNumber"] = phone,
                ["GroupName"] = groupName,
                ["GroupId"] = groupId,
                ["IsLocal"] = isLocal
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await _dialog.ShowAsync<DeleteUserDialog>("Delete User", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && onSuccess is not null)
                await onSuccess.Invoke();
        }
    }
}
