using WhatsAppAdmin.Models;

namespace WhatsAppAdmin.Services
{
    public class ImportStateService
    {
        private List<WhatsAppGroup> _groups = new();

        // Event fired when groups change
        public event Action? OnChange;

        public IReadOnlyList<WhatsAppGroup> Groups => _groups.AsReadOnly();

        public bool HasGroups => _groups.Count > 0;

        public void SetGroups(IEnumerable<WhatsAppGroup> groups)
        {
            _groups = new List<WhatsAppGroup>(groups);
            OnChange?.Invoke();
        }

        public void Clear()
        {
            _groups.Clear();
            OnChange?.Invoke();
        }
    }
}
