namespace WhatsAppAdmin.Models
{
    public class WhatsAppGroup
    {
        public string Id { get; set; } = string.Empty; // e.g. "1", "2a"
        public string Name { get; set; } = string.Empty;
        public List<Users> Users { get; set; } = new List<Users>();
    }
}
