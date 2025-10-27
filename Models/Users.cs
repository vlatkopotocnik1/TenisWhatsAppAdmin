namespace WhatsAppAdmin.Models
{
    public class Users
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Notes { get; set; } = string.Empty;
        public required string PhoneNumber { get; set; } // E.164 format
    }
}
