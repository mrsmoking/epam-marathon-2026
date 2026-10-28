using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupReservationService(
    IGroupReservationRepository groupReservationRepository,
    ITableRepository tableRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IUserService userService,
    ILocationService locationService,
    IOrderRepository orderRepository) : IGroupReservationService
{
    private const string DefaultRestaurantName = "Green & Tasty";

    public async Task<GetGroupReservationResponse> GetGroupReservationByTokenAsync(string token)
    {
        GroupReservation groupReservation = await groupReservationRepository.GetGroupReservationByTokenAsync(token)
            ?? throw new GroupReservationNotFoundException();

        if (groupReservation.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException();

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(groupReservation.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(groupReservation.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new ValidationException("Associated reservation not found.");
        int joinedGuestsCount = await countTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException();

        Table table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new InvalidOperationException("Associated table not found.");

        Location? location = await locationService.GetLocationByIdAsync(table.LocationId);
        string restaurantName = DefaultRestaurantName;
        string address = location?.Address ?? "Unknown Address";

        string inviterName = "Unknown";
        if (!string.IsNullOrWhiteSpace(reservation.CustomerId))
        {
            inviterName = await userService.GetUserName(reservation.CustomerId) ?? "Unknown";
        }
        return new GetGroupReservationResponse(
            groupReservation.GroupReservationId,
            table.LocationId,
            restaurantName,
            address,
            reservation.StartTime,
            reservation.EndTime,
            reservation.GuestCount,
            table.TableNumber,
            inviterName);
    }

    public async Task<GroupReservation> GetGroupReservationAsync(string groupReservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        return groupReservation ?? throw new InvalidOperationException($"Group reservation with ID {groupReservationId} not found.");
    }

    public async Task<GroupReservation> CreateGroupReservationAsync(string reservationId)
    {
        var groupReservation = new GroupReservation
        {
            GroupReservationId = Guid.NewGuid().ToString(),
            ReservationId = reservationId,
            ReservationToken = Guid.NewGuid().ToString("N"),
            Status = GroupReservationStatus.Open,
        };

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
        return groupReservation;
    }

    public async Task CreateGroupReservationIfDoesNotExistAsync(string userId, string reservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (group is not null)
            return;

        await CreateGroupReservationAsync(reservationId);
    }

    public async Task UpdateGroupReservationStatusAsync(string groupReservationId, GroupReservationStatus status)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        groupReservation.Status = status;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task DeleteGroupReservationAsync(string groupReservationId)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        await groupReservationRepository.DeleteGroupReservationAsync(groupReservation);
    }

    public async Task CloseGroupReservationAsync(string reservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (groupReservation is null)
            return;

        groupReservation.Status = GroupReservationStatus.Closed;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task<GetGroupInviteTokenResponse> GetGroupReservationInviteTokenAsync(string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (reservation.GuestCount < 2)
            throw new ValidationException("Retrieving invitation token for the reservation with only one guest is not permitted.");

        if (reservation.Status != ReservationStatus.Reserved)
            throw new GroupReservationExpiredException("The group reservation is expired.");

        GroupReservation groupReservation = (await groupReservationRepository.GetByReservationIdAsync(reservationId))!;

        if (groupReservation.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (groupReservation.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        return new GetGroupInviteTokenResponse(groupReservation.ReservationToken);
    }

    public async Task<List<GroupPreOrderItemResponse>> SubmitGroupPreOrderAsync(string userId, string groupReservationId, string reservationId)
    {
        GroupReservation group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        if (reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");

        if (group.ReservationId != reservationId)
            throw new ValidationException("Reservation ID does not match the group reservation.");

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        List<GuestSession> submittedSessions = sessions.Where(s => s.IsSubmitted).ToList();
        if (submittedSessions.Count == 0)
            throw new ValidationException("There are no submitted guest sessions in the group.");

        List<GroupPreOrderItemResponse> consolidated = ConsolidateDishes(submittedSessions);
        if (consolidated.Count == 0)
            throw new ValidationException("Submitted guest sessions contain no dishes.");

        Order? order = await orderRepository.GetOrderByReservationIdAsync(group.ReservationId);
        if (order is null || order.Status == OrderStatus.Cancelled)
            throw new ValidationException($"Active pre-order for reservation '{group.ReservationId}' not found.");

        // Atomic claim (Open -> Submitted): a concurrent or repeated submit cannot merge the dishes twice.
        bool claimed = await groupReservationRepository.TryUpdateStatusAsync(
            groupReservationId, GroupReservationStatus.Open, GroupReservationStatus.Submitted);
        if (!claimed)
            throw new GroupReservationSubmittedException();

        MergeIntoOrder(order, consolidated);
        order.Status = OrderStatus.SubmittedByCustomer;

        try
        {
            await orderRepository.SaveOrderAsync(order);
        }
        catch
        {
            // Best effort: release the claim so the owner can retry.
            try
            {
                await groupReservationRepository.TryUpdateStatusAsync(
                    groupReservationId, GroupReservationStatus.Submitted, GroupReservationStatus.Open);
            }
            catch (Exception)
            {
                // Ignore: the original exception is more relevant.
            }

            throw;
        }

        return consolidated;
    }

    private static List<GroupPreOrderItemResponse> ConsolidateDishes(IEnumerable<GuestSession> sessions) =>
        sessions
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => d.Quantity > 0 && !string.IsNullOrWhiteSpace(d.DishId))
            .GroupBy(d => d.DishId)
            .Select(g => new GroupPreOrderItemResponse(g.Key, g.Sum(d => d.Quantity)))
            .ToList();

    private static void MergeIntoOrder(Order order, IEnumerable<GroupPreOrderItemResponse> items)
    {
        order.Dishes ??= [];

        foreach (GroupPreOrderItemResponse item in items)
        {
            // Never add a second line for the same dish: waiter confirm/cancel look up the first line by DishId.
            OrderItem? existing = order.Dishes.FirstOrDefault(d => d.DishId == item.DishId);
            if (existing is not null)
            {
                // A cancelled line is revived with the new quantity; any other line gets the quantity added.
                // A changed line must be re-confirmed by the waiter, so it goes back to Pending.
                existing.Quantity = existing.State == OrderItemState.Canceled
                    ? item.Quantity
                    : existing.Quantity + item.Quantity;
                existing.State = OrderItemState.Pending;
                continue;
            }

            order.Dishes.Add(new OrderItem
            {
                DishId = item.DishId,
                Quantity = item.Quantity,
                State = OrderItemState.Pending,
            });
        }
    }
}

