using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary><paramref name="who"/> asks the service to send the group pre-order of <paramref name="target"/> to the
/// restaurant. Whatever the answer, it is left as the scenario's last response; a set-up step passes
/// <paramref name="mustSucceed"/> so a failed submit stops the scenario there.</summary>
public sealed class SubmitTheGroupPreOrder(
    Someone who = Someone.TheOwner, GroupTarget target = GroupTarget.TheGroup, bool mustSucceed = false) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var groupReservationId = target == GroupTarget.AGroupThatDoesNotExist ? Guid.NewGuid().ToString() : booking.GroupReservationId;
        var reservationId = target == GroupTarget.ABookingThatDoesNotExist ? Guid.NewGuid().ToString() : booking.ReservationId;
        var token = await actor.AsksForAsync(new TheBearerTokenOf(who));
        var response = actor.TheLastResponse();

        await response.CaptureAsync(
            await new GroupPreOrdersApi(actor.UsingTheApi()).SubmitAsync(groupReservationId, reservationId, token));
        if (mustSucceed && !response.Succeeded)
            throw new InvalidOperationException($"Submitting the group pre-order failed: {response.Status} {response.Body}");
    }
}

/// <summary>The owner opens the group pre-order screen, presses "Submit Pre-Order" and confirms in the dialog, then waits
/// for the service's answer (success or refusal — the screen shows either).</summary>
public sealed class SubmitTheGroupPreOrderOnTheScreen : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        await actor.AttemptsToAsync(new OpenTheGroupOrder());
        await new GroupOrderPage(actor.LookingAtThePage()).SubmitAsync();
    }
}
