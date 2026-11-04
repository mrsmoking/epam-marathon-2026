using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Sending the group pre-order to the restaurant, and what the restaurant and the waiter get. What the
/// restaurant gets is read from the service in either stage; the screens are checked in `@ui` scenarios.</summary>
[Binding]
public sealed class GroupSubmitStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Given("the owner has pre-ordered {int} {string}")]
    public Task GivenTheOwnerHasPreOrdered(int quantity, string dish) =>
        Actor.AttemptsToAsync(new PreOrderAsTheOwner(dish, quantity));

    [Given("the owner has submitted the group pre-order")]
    public Task GivenTheOwnerHasSubmittedTheGroupPreOrder() =>
        Actor.AttemptsToAsync(new SubmitTheGroupPreOrder(mustSucceed: true));

    [When("{someone} submits the group pre-order")]
    [Scope(Tag = "api")]
    public Task WhenSomeoneSubmitsTheGroupPreOrder(Someone someone) =>
        Actor.AttemptsToAsync(new SubmitTheGroupPreOrder(someone));

    /// <summary>Only the owner has the screen with the Submit button; everyone else is checked through the service.</summary>
    [When("{someone} submits the group pre-order")]
    [Scope(Tag = "ui")]
    public Task WhenSomeoneSubmitsTheGroupPreOrderOnTheScreen(Someone someone) =>
        someone == Someone.TheOwner
            ? Actor.AttemptsToAsync(new SubmitTheGroupPreOrderOnTheScreen())
            : throw new InvalidOperationException($"Only the owner has a group pre-order screen, not {someone}.");

    [When("the owner submits the pre-order of a group that does not exist")]
    public Task WhenTheOwnerSubmitsAGroupThatDoesNotExist() =>
        Actor.AttemptsToAsync(new SubmitTheGroupPreOrder(target: GroupTarget.AGroupThatDoesNotExist));

    [When("the owner submits the group pre-order for a booking that does not exist")]
    public Task WhenTheOwnerSubmitsForABookingThatDoesNotExist() =>
        Actor.AttemptsToAsync(new SubmitTheGroupPreOrder(target: GroupTarget.ABookingThatDoesNotExist));

    [When("the waiter opens the pre-order of the booking")]
    public Task WhenTheWaiterOpensThePreOrder() => Actor.AttemptsToAsync(new OpenTheWaitersPreOrder());

    // ---- what the restaurant gets ----

    [Then("the restaurant gets the pre-order:")]
    public async Task ThenTheRestaurantGetsThePreOrder(DataTable dishes)
    {
        var order = await Actor.AsksForAsync(new TheOrderTheRestaurantGets());
        await AssertDishes(order.Quantities, dishes, $"the pre-order was: {order.Body}");
    }

    [Then("the pre-order is sent to the restaurant")]
    public async Task ThenThePreOrderIsSentToTheRestaurant()
    {
        var order = await Actor.AsksForAsync(new TheOrderTheRestaurantGets());
        await Assert.That(order.Status).IsEqualTo("SubmittedByCustomer").Because($"the pre-order was: {order.Body}");
        await Assert.That(await Actor.AsksForAsync(new TheGroupStatus())).IsEqualTo("Submitted");
    }

    [Then("the group pre-order is still open")]
    public async Task ThenTheGroupPreOrderIsStillOpen() =>
        await Assert.That(await Actor.AsksForAsync(new TheGroupStatus())).IsEqualTo("Open")
            .Because("nothing should have been sent to the restaurant");

    // ---- what the screens show ----

    [Then("the owner sees that the pre-order was sent to the restaurant")]
    public async Task ThenTheOwnerSeesThePreOrderWasSent()
    {
        var standing = await Actor.AsksForAsync(new TheGroupOrderStanding());
        var because = $"the screen showed {standing}";

        await Assert.That(standing.StatusBadge).IsEqualTo("Sent to Restaurant").Because(because);
        await Assert.That(standing.Sent).IsTrue().Because(because);
        await Assert.That(standing.Success).IsEqualTo("Pre-order sent to the restaurant").Because(because);
    }

    [Then("the consolidated pre-order on the screen is:")]
    public async Task ThenTheConsolidatedPreOrderOnTheScreenIs(DataTable dishes)
    {
        var shown = await Actor.AsksForAsync(new TheConsolidatedPreOrderShown());
        await AssertDishes(shown.Quantities, dishes, shown.Shown);
    }

    [Then("the owner cannot send the pre-order yet")]
    public async Task ThenTheOwnerCannotSendThePreOrderYet()
    {
        var standing = await Actor.AsksForAsync(new TheGroupOrderStanding());
        await Assert.That(standing.CanSubmit).IsFalse().Because($"the screen showed {standing}");
    }

    [Then("the waiter sees the dishes:")]
    public async Task ThenTheWaiterSeesTheDishes(DataTable dishes)
    {
        var shown = await Actor.AsksForAsync(new TheWaitersPreOrderShown());
        await AssertDishes(shown.Quantities, dishes, shown.Shown);
    }

    private static async Task AssertDishes(IReadOnlyDictionary<string, int> actual, DataTable expected, string because)
    {
        var wanted = expected.Rows.ToDictionary(
            row => row["dish"],
            row => int.Parse(row["quantity"], System.Globalization.CultureInfo.InvariantCulture));

        await Assert.That(actual.Count).IsEqualTo(wanted.Count).Because(because);
        foreach (var (dish, quantity) in wanted)
            await Assert.That(actual.GetValueOrDefault(dish)).IsEqualTo(quantity).Because($"{dish}: {because}");
    }
}
