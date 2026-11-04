using System.Text.Json;
using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The booking's pre-order as the restaurant has it: its lines ("3 x Borscht", by the short names the scenario
/// put on the menu) and its status. Read from the service with the owner's token, whatever stage the scenario runs in.</summary>
public sealed record RestaurantOrder(IReadOnlyDictionary<string, int> Quantities, string Status, string Body);

public sealed class TheOrderTheRestaurantGets : IQuestion<RestaurantOrder>
{
    public async Task<RestaurantOrder> AnsweredByAsync(Actor actor)
    {
        var owner = await actor.AsksForAsync(new TheCustomerSession(Personas.Owner));
        var booking = actor.Recall<GroupBooking>();
        var response = await new OrdersApi(actor.UsingTheApi()).GetOrderAsync(booking.ReservationId, owner.IdToken);
        var body = await response.TextAsync();
        if (!response.Ok)
            throw new InvalidOperationException($"Reading the pre-order failed with {response.Status}: {body}");

        var aliases = actor.RecallAll<MenuDish>().ToDictionary(d => d.Dish.Id, d => d.Alias);
        var order = JsonSerializer.Deserialize<JsonElement>(body);

        var quantities = new Dictionary<string, int>();
        foreach (var line in order.GetProperty("orderItems").EnumerateArray())
        {
            var dish = line.GetProperty("dish");
            var name = aliases.GetValueOrDefault(dish.GetProperty("id").GetString()!, dish.GetProperty("name").GetString()!);
            quantities[name] = quantities.GetValueOrDefault(name) + line.GetProperty("quantity").GetInt32();
        }

        return new RestaurantOrder(quantities, StatusName<Api.Dtos.Orders.OrderStatus>(order.GetProperty("status")), body);
    }

    /// <summary>Statuses come as the enum's number (no string converter on the backend) or, should that change, its name.</summary>
    internal static string StatusName<TEnum>(JsonElement status) where TEnum : struct, Enum =>
        status.ValueKind == JsonValueKind.Number
            ? ((TEnum)Enum.ToObject(typeof(TEnum), status.GetInt32())).ToString()
            : status.GetString() ?? "";
}

/// <summary>The status of the scenario's group pre-order: "Open" while guests are choosing, "Submitted" once sent.</summary>
public sealed class TheGroupStatus : IQuestion<string>
{
    private enum GroupReservationStatus
    {
        Open,
        Submitted,
        Closed,
    }

    public async Task<string> AnsweredByAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var response = await new GroupPreOrdersApi(actor.UsingTheApi()).GetStatusAsync(booking.GroupReservationId);
        var body = await response.TextAsync();
        if (!response.Ok)
            throw new InvalidOperationException($"Reading the group status failed with {response.Status}: {body}");

        return TheOrderTheRestaurantGets.StatusName<GroupReservationStatus>(
            JsonSerializer.Deserialize<JsonElement>(body).GetProperty("status"));
    }
}
