using TenisWhatsAppAdmin.Models;

namespace TenisWhatsAppAdmin.Services
{
    public interface ILeagueService
    {
        /// <summary>
        /// Get all leagues from external API (or mock)
        /// </summary>
        Task<List<League>> GetLeaguesAsync();


        /// <summary>
        /// Persist change of a single player to a target league. Returns updated leagues.
        /// </summary>
        Task<List<League>> MovePlayerAsync(Guid playerId, string fromLeagueId, string toLeagueId);


        /// <summary>
        /// Optional: save all leagues
        /// </summary>
        Task<List<League>> SaveLeaguesAsync(List<League> leagues);
    }
}
