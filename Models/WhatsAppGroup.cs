namespace WhatsAppAdmin.Models
{
    public class WhatsAppGroup
    {
        public string Id { get; set; } = string.Empty; // e.g. "1", "2a"
        public string Name { get; set; } = string.Empty;
        public List<User> Users { get; set; } = new List<User>();

        public bool IsNew { get; set; } = false;
        public List<User> ToAdd { get; set; } = new();
        public List<User> ToRemove { get; set; } = new();
    }
}
