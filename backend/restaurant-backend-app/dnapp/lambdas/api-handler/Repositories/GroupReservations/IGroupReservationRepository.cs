using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupReservations;

public interface IGroupReservationRepository
{
    Task<GroupReservation?> GetByReservationIdAsync(string reservationId);
    Task<GroupReservation?> GetGroupReservationByIdAsync(string groupReservationId);
    Task DeleteGroupReservationAsync(GroupReservation groupReservation);
    Task SaveGroupReservationAsync(GroupReservation groupReservation);
    Task<GroupReservation?> GetGroupReservationByTokenAsync(string token);

    /// <summary>
    /// Atomically changes the status only if the current stored status equals <paramref name="expectedStatus"/>.
    /// Returns false if the condition was not met (or the item does not exist).
    /// </summary>
    Task<bool> TryUpdateStatusAsync(string groupReservationId, GroupReservationStatus expectedStatus, GroupReservationStatus newStatus);
}
