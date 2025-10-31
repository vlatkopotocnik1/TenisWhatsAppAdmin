namespace WhatsAppAdmin.Services
{
    public static class GlobalEvents
    {
        public static event Action? OnHideContextMenu;
        public static void RaiseHideContextMenu() => OnHideContextMenu?.Invoke();
    }
}
