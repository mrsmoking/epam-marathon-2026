using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest joins the current group pre-order under their name and is remembered by it. Nothing to remove
/// afterwards: cancelling the booking closes the group and every session in it.</summary>
public sealed class JoinTheGroup(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GuestSessionsApi(actor.UsingTheApi())
            .JoinAsync(new JoinGroupRequest(group.GroupReservationId, guestName)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"{guestName} could not join the group: {response.Status} {response.Body}");

        var joined = response.Json<JoinGroupResponse>();
        actor.Remember(guestName, new GuestSession(guestName, joined.SessionId, joined.SessionToken));
    }
}
