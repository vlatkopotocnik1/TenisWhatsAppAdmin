using WhatsAppAdmin.Models;

namespace WhatsAppAdmin.Services
{
    public interface IService
    {
        /// <summary>
        /// Get all whatsAppGroups from external API (or mock)
        /// </summary>
        Task<List<WhatsAppGroup>> GetWhatsAppGroupsAsync();


        /// <summary>
        /// Persist change of a single user to a target whatsAppGroup. Returns updated whatsAppGroups.
        /// </summary>
        Task<List<WhatsAppGroup>> MoveUserAsync(Guid userId, string fromWhatsAppGroupId, string toWhatsAppGroupId);


        /// <summary>
        /// Optional: save all whatsAppGroups
        /// </summary>
        Task<List<WhatsAppGroup>> SaveWhatsAppGroupsAsync(List<WhatsAppGroup> whatsAppGroups);
    }
}
