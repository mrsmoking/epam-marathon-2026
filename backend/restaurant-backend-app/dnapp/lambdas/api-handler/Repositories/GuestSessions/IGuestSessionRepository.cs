using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public interface IGuestSessionRepository
{
    Task<int> CountByGroupReservationId(string groupReservationId);

    Task<GuestSession?> GetByGroupReservationId(string groupReservationId);

    Task<GuestSession?> GetByGuestSessionId(string id);

    Task<GuestSession?> GetByTokenHash(string tokenHash);

    Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId);

    Task SaveAsync(GuestSession guestSession);

    /// <summary>
    /// Overwrites an existing session; returns false if the session no longer exists (e.g. it was deleted).
    /// </summary>
    Task<bool> TryUpdateExistingAsync(GuestSession guestSession);

    Task DeleteAsync(GuestSession session);
}
