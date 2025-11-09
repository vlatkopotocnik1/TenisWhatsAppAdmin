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

        private async Task OpenBroadcastDialog(string? id = null, string? name = null)
        {
            await Dialogs.OpenBroadcastDialogAsync(id, name, async () =>
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            });
        }

        private async Task OpenRenameGroupDialog(string id, string oldName, bool isLocal = false)
        {
            await Dialogs.OpenRenameGroupDialogAsync(id, oldName, isLocal, async () =>
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            });
        }

        private async Task OpenAddUserDialog(string groupName, bool isLocal = false)
        {
            await Dialogs.OpenAddUserDialogAsync(groupName, isLocal, async () =>
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            });
        }

        private async Task OpenDeleteGroupDialog(string id, string name, bool isLocal = false)
        {
            await Dialogs.OpenDeleteGroupDialogAsync(id, name, isLocal, async () =>
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            });
        }

        private async Task OpenDeleteUserDialog(string phone, string groupId, string groupName, bool isLocal = false)
        {
            await Dialogs.OpenDeleteUserDialogAsync(phone, groupId, groupName, isLocal, async () =>
            {
                await RefreshGroupsFromWhatsAppAsync();
                StateHasChanged();
            });
        }

        public void Dispose() => GroupSync.Dispose();
    }
}
