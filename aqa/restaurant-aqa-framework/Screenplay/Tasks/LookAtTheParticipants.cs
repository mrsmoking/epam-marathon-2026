using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary><paramref name="who"/> asks for the participants of the current group (or of one that does not exist).
/// Whatever the answer, it is left as the scenario's last response — refusals are what some scenarios test.</summary>
public sealed class LookAtTheParticipants(Someone who, bool ofAGroupThatDoesNotExist = false) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var groupReservationId = ofAGroupThatDoesNotExist
            ? Guid.NewGuid().ToString()
            : actor.Recall<GroupBooking>().GroupReservationId;
        var token = await actor.AsksForAsync(new TheBearerTokenOf(who));

        await actor.TheLastResponse().CaptureAsync(
            await new GroupPreOrdersApi(actor.UsingTheApi()).GetParticipantsAsync(groupReservationId, token));
    }
}
