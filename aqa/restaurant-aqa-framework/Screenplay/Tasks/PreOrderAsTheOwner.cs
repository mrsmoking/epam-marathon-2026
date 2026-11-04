using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The owner pre-orders a dish (by its short name) for the booking: the first dish creates the pre-order,
/// later ones edit it. A group can only be submitted into an existing pre-order. Nothing to remove afterwards: the
/// pre-order stays with the booking, which is cancelled when the scenario ends.</summary>
public sealed class PreOrderAsTheOwner(string dishAlias, int quantity) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var owner = await actor.AsksForAsync(new TheCustomerSession(Personas.Owner));
        var booking = actor.Recall<GroupBooking>();
        var dish = actor.Recall<MenuDish>(dishAlias).Dish;
        var api = new OrdersApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        var hasPreOrder = actor.TryRecall<OwnerPreOrder>(out var preOrder);
        var items = (hasPreOrder ? preOrder.Items : Array.Empty<OrderItemRequest>())
            .Append(new OrderItemRequest(dish.Id, quantity))
            .ToList();

        await response.CaptureAsync(hasPreOrder
            ? await api.EditOrderAsync(booking.ReservationId, new EditPreOrderRequest(items), owner.IdToken)
            : await api.CreateOrderAsync(new CreateOrderRequest(booking.ReservationId, items), owner.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"The owner could not pre-order {quantity} x {dishAlias}: {response.Status} {response.Body}");

        actor.Remember(new OwnerPreOrder(items));
    }
}
