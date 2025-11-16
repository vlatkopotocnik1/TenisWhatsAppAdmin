using Microsoft.AspNetCore.Components;
using MudBlazor;
using WhatsAppAdmin.Shared.Dialogs;

namespace WhatsAppAdmin.Helpers
{
    public class DialogManager(IDialogService dialog)
    {
        private readonly IDialogService _dialog = dialog;

        private async Task<bool> ShowDialogAsync<T>(
            string title,
            DialogParameters? parameters,
            MaxWidth maxWidth,
            Func<Task>? onSuccess = null
        ) where T : ComponentBase
        {
            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = maxWidth,
                FullWidth = true
            };

            // Ensure parameters is never null
            var safeParameters = parameters ?? [];

            var dialogRef = await _dialog.ShowAsync<T>(title, safeParameters, options);
            var result = await dialogRef.Result;

            if (result is not null && !result.Canceled)
            {
                if (onSuccess is not null)
                {
                    await onSuccess.Invoke();
                }

                return true;
            }

            return false;
        }

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

            await ShowDialogAsync<BroadcastDialog>(title, parameters, MaxWidth.Medium, onSuccess);
        }

        public async Task OpenRenameGroupDialogAsync(string id, string oldName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupId"] = id,
                ["OldName"] = oldName,
                ["IsLocal"] = isLocal
            };

            await ShowDialogAsync<RenameGroupDialog>("Rename Group", parameters, MaxWidth.Small, onSuccess);
        }

        public async Task OpenAddUserDialogAsync(string groupName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = groupName,
                ["IsLocal"] = isLocal
            };

            await ShowDialogAsync<AddUserDialog>($"Add User to {groupName}", parameters, MaxWidth.Small, onSuccess);
        }

        public async Task OpenDeleteGroupDialogAsync(string id, string name, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["GroupName"] = name,
                ["GroupId"] = id,
                ["IsLocal"] = isLocal
            };

            await ShowDialogAsync<DeleteGroupDialog>("Delete All Users", parameters, MaxWidth.Small, onSuccess);
        }

        public async Task OpenDeleteUserDialogAsync(string phone, string groupId, string groupName, bool isLocal, Func<Task>? onSuccess = null)
        {
            var parameters = new DialogParameters
            {
                ["PhoneNumber"] = phone,
                ["GroupName"] = groupName,
                ["GroupId"] = groupId,
                ["IsLocal"] = isLocal
            };

            await ShowDialogAsync<DeleteUserDialog>("Delete User", parameters, MaxWidth.Small, onSuccess);
        }
    }
}
