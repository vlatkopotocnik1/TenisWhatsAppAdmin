namespace TenisWhatsAppAdmin.Models
{
    public class Player
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Notes { get; set; } = string.Empty;
        public int Played { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
    }
}
