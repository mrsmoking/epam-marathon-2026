using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Users;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>Where the standing waiter works — looked up once per scenario, then remembered.</summary>
public sealed class TheWaitersLocation : IQuestion<WaiterLocation>
{
    public async Task<WaiterLocation> AnsweredByAsync(Actor actor)
    {
        if (actor.TryRecall<WaiterLocation>(out var location))
            return location;

        var waiter = await actor.AsksForAsync(new TheWaiterSession());
        var api = new WaitersApi(actor.UsingTheApi());

        var me = await api.GetMeAsync(waiter.IdToken);
        if (!me.Ok)
            throw new InvalidOperationException($"Reading the waiter's profile failed with {me.Status}: {await me.TextAsync()}");

        var userId = (await me.ReadAsAsync<MeResponse>()).Id;
        var response = await api.GetWaiterLocationAsync(userId, waiter.IdToken);
        if (!response.Ok)
            throw new InvalidOperationException(
                $"Looking up the waiter's location failed with {response.Status}: {await response.TextAsync()}");

        location = new WaiterLocation((await response.ReadAsAsync<WaiterLocationResponse>()).LocationId);
        actor.Remember(location);
        return location;
    }
}
