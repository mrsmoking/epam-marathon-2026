using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Contexts;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The owner books a table for a group: a sample table of exactly that size is added at the waiter's
/// location (so no other scenario can collide with it), the owner books it and opens the invite, which leads to
/// the group pre-order. Removal runs in reverse: the booking is cancelled first, then the table is deleted.
/// By default the owner books and the group becomes the scenario's current one; another customer's booking is
/// remembered under <paramref name="groupName"/> and leaves the current group as it was.</summary>
public sealed class BookATableForTheGroup(int guestCount, string persona = Personas.Owner, string? groupName = null) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var owner = await actor.AsksForAsync(new TheCustomerSession(persona));
        var waiter = await actor.AsksForAsync(new TheWaiterSession());
        var location = await actor.AsksForAsync(new TheWaitersLocation());
        var cleanup = actor.Recall<CleanupScope>();
        var response = actor.TheLastResponse();

        var tables = new TablesApi(actor.UsingTheApi());
        await response.CaptureAsync(await tables.CreateTableAsync(
            SampleDataFactory.CreateTableRequest(location.LocationId, guestCount), waiter.IdToken));
        Ensure(response.Succeeded, "Adding the table", response);
        var table = response.Json<TableResponse>();
        cleanup.TrackDelete($"table:{table.Id}", () => tables.DeleteTableAsync(table.Id, waiter.IdToken));

        var reservations = new ReservationsApi(actor.UsingTheApi());
        await response.CaptureAsync(await reservations.CreateReservationAsync(
            SampleDataFactory.CreateReservationRequest(table.Id, SampleDataFactory.GroupBookingStart(), guestCount),
            owner.IdToken));
        Ensure(response.Succeeded, "Booking the table", response);
        var reservation = response.Json<ReservationResponse>();
        cleanup.TrackDelete(
            $"reservation:{reservation.Id}",
            () => reservations.CancelReservationAsync(reservation.Id, owner.IdToken));

        await response.CaptureAsync(
            await new GroupPreOrdersApi(actor.UsingTheApi()).GetInviteTokenAsync(reservation.Id, owner.IdToken));
        Ensure(response.Succeeded, "Getting the invite", response);
        var inviteToken = response.Json<InviteTokenResponse>().Token;

        await response.CaptureAsync(await new GuestSessionsApi(actor.UsingTheApi()).GetInviteAsync(inviteToken));
        Ensure(response.Succeeded, "Opening the invite", response);
        var invite = response.Json<InviteResponse>();

        var booking = new GroupBooking(reservation.Id, invite.GroupReservationId, inviteToken, guestCount);
        if (groupName is null)
        {
            actor.Remember(booking);
            return;
        }

        // A named group is also made "the current" one by memory; put the scenario's own group back in front.
        var current = actor.TryRecall<GroupBooking>(out var own) ? own : null;
        actor.Remember(groupName, booking);
        if (current is not null)
            actor.Remember(current);
    }

    private static void Ensure(bool succeeded, string what, ApiResponseContext response)
    {
        if (!succeeded)
            throw new InvalidOperationException($"{what} for the group failed with {response.Status}: {response.Body}");
    }
}
