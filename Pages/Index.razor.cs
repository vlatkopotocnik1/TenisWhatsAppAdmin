using Microsoft.AspNetCore.Components;
using WhatsAppAdmin.Models;
using WhatsAppAdmin.Services;
using WhatsAppAdmin.Helpers;

namespace WhatsAppAdmin.Pages
{
    public partial class Index : IDisposable
    {
        [Inject] private GroupSyncService GroupSync { get; set; } = default!;
        [Inject] private DialogManager Dialogs { get; set; } = default!;

        private List<WhatsAppGroup> _whatsAppGroups = [];

        protected override async Task OnInitializedAsync()
        {
            _whatsAppGroups = await GroupSync.InitializeAsync();
        }

        private async Task SyncGroupsOnWhatsApp()
        {
            await GroupSync.SyncAsync();
            _whatsAppGroups = await GroupSync.RefreshAsync();
            StateHasChanged();
        }

        private async Task DeleteAllGroupsFromWhatsApp()
        {
            await GroupSync.DeleteAllAsync();
            _whatsAppGroups = await GroupSync.RefreshAsync();
            StateHasChanged();
        }

        private async Task RefreshGroupsFromWhatsAppAsync()
        {
            _whatsAppGroups = await GroupSync.RefreshAsync();
        }

        private static string GetGroupStatus(WhatsAppGroup g)
        {
            if (g.IsNew) return "new";
            if (g.ToAdd.Count != 0 || g.ToRemove.Count != 0) return "changed";
            return "unchanged";
        }

        // === Dialog openings with UI refresh callbacks ===
        private async Task RefreshAfterDialog(Func<Task> dialogAction)
        {
            await dialogAction();
            await RefreshGroupsFromWhatsAppAsync();
            StateHasChanged();
        }

        private async Task OpenBroadcastDialog(string? id = null, string? name = null)
        {
            await RefreshAfterDialog(() => Dialogs.OpenBroadcastDialogAsync(id, name));
        }

        private async Task OpenRenameGroupDialog(string id, string oldName, bool isLocal = false)
        {
            await RefreshAfterDialog(() => Dialogs.OpenRenameGroupDialogAsync(id, oldName, isLocal));
        }

        private async Task OpenAddUserDialog(string groupName, bool isLocal = false)
        {
            await RefreshAfterDialog(() => Dialogs.OpenAddUserDialogAsync(groupName, isLocal));
        }

        private async Task OpenDeleteGroupDialog(string id, string name, bool isLocal = false)
        {
            await RefreshAfterDialog(() => Dialogs.OpenDeleteGroupDialogAsync(id, name, isLocal));
        }

        private async Task OpenDeleteUserDialog(string phone, string groupId, string groupName, bool isLocal = false)
        {
            await RefreshAfterDialog(() => Dialogs.OpenDeleteUserDialogAsync(phone, groupId, groupName, isLocal));
        }

        public void Dispose()
        {
            GroupSync.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
