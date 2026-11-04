using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Puts a sample dish at the given price on the menu of the waiter's location and remembers it under the
/// short name the feature file uses, so guests can order it by that name.</summary>
public sealed class PutDishOnTheMenu(string alias, decimal price) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var location = await actor.AsksForAsync(new TheWaitersLocation());
        var api = new DishesApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        // The short name plus a run-unique suffix: readable on the screen, never confused with a parallel scenario's dish.
        var request = SampleDataFactory.CreateDishRequest([location.LocationId]) with
        {
            Name = $"{SampleDataFactory.Prefix}{alias}-{Guid.NewGuid():N}"[..(SampleDataFactory.Prefix.Length + alias.Length + 9)],
            Price = price,
        };
        await response.CaptureAsync(await api.CreateDishAsync(request));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Putting '{alias}' on the menu failed with {response.Status}: {response.Body}");

        var dish = response.Json<DishResponse>();
        actor.Remember(alias, new MenuDish(alias, dish));

        var waiter = await actor.AsksForAsync(new TheWaiterSession());
        actor.Recall<CleanupScope>().TrackDelete(
            $"dish:{dish.Id}",
            () => api.DeleteDishAsync(dish.Id, force: true, bearerToken: waiter.IdToken));
    }
}
