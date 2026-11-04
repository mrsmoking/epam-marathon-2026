using System.Text.Json;
using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Who is to be removed from the group.</summary>
public enum Removal
{
    /// <summary>The guest named in the task.</summary>
    TheGuest,

    /// <summary>A session id nobody has.</summary>
    ANonExistentParticipant,

    /// <summary>The owner's own session in the group.</summary>
    TheOwner,
}

/// <summary><paramref name="who"/> asks the service to remove a participant from the current group. The answer — the
/// updated list of participants, or a refusal — is left as the scenario's last response; a set-up step passes
/// <paramref name="mustSucceed"/> so a failed removal stops the scenario there.</summary>
public sealed class RemoveTheParticipant(
    Someone who, string? guestName = null, Removal removal = Removal.TheGuest, bool mustSucceed = false) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var api = new GroupPreOrdersApi(actor.UsingTheApi());
        var token = await actor.AsksForAsync(new TheBearerTokenOf(who));

        var sessionId = removal switch
        {
            Removal.TheGuest => actor.Recall<GuestSession>(guestName!).SessionId,
            Removal.ANonExistentParticipant => Guid.NewGuid().ToString(),
            Removal.TheOwner => await OwnerSessionIdAsync(actor, api, booking),
            _ => throw new InvalidOperationException($"Unknown removal '{removal}'."),
        };

        var response = actor.TheLastResponse();
        await response.CaptureAsync(await api.RemoveParticipantAsync(booking.GroupReservationId, sessionId, token));
        if (mustSucceed && !response.Succeeded)
            throw new InvalidOperationException($"Removing {guestName} failed: {response.Status} {response.Body}");
    }

    private static async Task<string> OwnerSessionIdAsync(Actor actor, GroupPreOrdersApi api, GroupBooking booking)
    {
        var owner = await actor.AsksForAsync(new TheCustomerSession(Personas.Owner));
        var response = await api.GetOwnerSessionIdAsync(booking.ReservationId, owner.IdToken);
        var body = await response.TextAsync();
        if (!response.Ok)
            throw new InvalidOperationException($"Getting the owner's session failed with {response.Status}: {body}");

        return JsonSerializer.Deserialize<OwnerSessionResponse>(body, JsonDefaults.Options)!.GuestSessionId;
    }
}

/// <summary>The owner presses "Remove Guest" on the guest's card on the group pre-order screen, confirms in the
/// dialog and waits for the updated list.</summary>
public sealed class RemoveTheParticipantOnTheScreen(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();
        await page.RemoveGuestAsync(guestName);
    }
}
