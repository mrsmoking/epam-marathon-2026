using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest adds a dish from the menu (by its short name) to their own cart.</summary>
public sealed class AddToTheCart(string guestName, string dishAlias, int quantity) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<GuestSession>(guestName);
        var dish = actor.Recall<MenuDish>(dishAlias).Dish;
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GuestSessionsApi(actor.UsingTheApi())
            .AddDishAsync(guest.SessionId, new AddGuestDishRequest(dish.Id, quantity), guest.SessionToken));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"{guestName} could not add {quantity} x {dishAlias}: {response.Status} {response.Body}");
    }
}

/// <summary>A guest takes some portions of a dish (by its short name) out of their own cart.</summary>
public sealed class TakeOutOfTheCart(string guestName, string dishAlias, int quantity) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<GuestSession>(guestName);
        var dish = actor.Recall<MenuDish>(dishAlias).Dish;
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GuestSessionsApi(actor.UsingTheApi())
            .RemoveDishAsync(guest.SessionId, new RemoveGuestDishRequest(dish.Id, quantity), guest.SessionToken));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"{guestName} could not remove {quantity} x {dishAlias}: {response.Status} {response.Body}");
    }
}

/// <summary>A guest confirms their cart, so it goes into the group pre-order when the owner submits it.</summary>
public sealed class SubmitTheCart(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<GuestSession>(guestName);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GuestSessionsApi(actor.UsingTheApi())
            .SubmitCartAsync(guest.SessionId, guest.SessionToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"{guestName} could not submit the cart: {response.Status} {response.Body}");
    }
}
