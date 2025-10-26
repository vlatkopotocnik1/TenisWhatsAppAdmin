namespace TenisWhatsAppAdmin.Models
{
    public class League
    {
        public string Id { get; set; } = string.Empty; // e.g. "1", "2a"
        public string Name { get; set; } = string.Empty;
        public List<Player> Players { get; set; } = new List<Player>();
    }
}
