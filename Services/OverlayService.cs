namespace WhatsAppAdmin.Services
{
    public class OverlayService
    {
        public event Action<bool, string>? OnChange;

        public void Show(string message = "Loading...")
            => OnChange?.Invoke(true, message);

        public void Hide()
            => OnChange?.Invoke(false, "");

        public async Task RunAsync(Func<Task> action, string message = "Loading...")
        {
            Show(message);
            try
            {
                await action();
            }
            finally
            {
                Hide();
            }
        }
        public async Task<T> RunAsync<T>(Func<Task<T>> action, string message = "Loading...")
        {
            Show(message);
            try
            {
                return await action();
            }
            finally
            {
                Hide();
            }
        }
    }
}
