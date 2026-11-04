using System.Globalization;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>The group pre-order: the owner books a table for a group, guests join and fill their carts, and the owner
/// looks at who has joined and what it all comes to. Who is who lives in the actor's memory: the owner and other
/// customers by persona, guests by name, dishes by the short name the feature file gives them. The same sentence is
/// a request to the service in an `@api` scenario and the owner's screen in a `@ui` one.</summary>
[Binding]
public sealed class GroupPreOrderStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Given("the restaurant's menu has the dishes:")]
    public async Task GivenTheMenuHasTheDishes(DataTable dishes)
    {
        foreach (var row in dishes.Rows)
            await Actor.AttemptsToAsync(new PutDishOnTheMenu(row["dish"], ParseAmount(row["price"])));
    }

    [Given("a customer has booked a table for {int} guests")]
    public Task GivenACustomerHasBookedATableFor(int guests) => Actor.AttemptsToAsync(new BookATableForTheGroup(guests));

    [Given("{string} has joined the group")]
    [When("{string} joins the group")]
    public Task GivenGuestHasJoinedTheGroup(string guest) => Actor.AttemptsToAsync(new JoinTheGroup(guest));

    [Given("{string} has added {int} {string} to the cart")]
    [When("{string} adds {int} {string} to the cart")]
    public Task GivenGuestHasAddedToTheCart(string guest, int quantity, string dish) =>
        Actor.AttemptsToAsync(new AddToTheCart(guest, dish, quantity));

    [Given("{string} has taken {int} {string} out of the cart")]
    [When("{string} takes {int} {string} out of the cart")]
    public Task WhenGuestTakesOutOfTheCart(string guest, int quantity, string dish) =>
        Actor.AttemptsToAsync(new TakeOutOfTheCart(guest, dish, quantity));

    [Given("{string} has submitted the cart")]
    [When("{string} submits the cart")]
    public Task GivenGuestHasSubmittedTheCart(string guest) => Actor.AttemptsToAsync(new SubmitTheCart(guest));

    [When("{string} tries to submit the cart")]
    public Task WhenGuestTriesToSubmitTheCart(string guest) =>
        Actor.AttemptsToAsync(new SubmitTheCart(guest, mustSucceed: false));

    [When("{string} tries to take {int} {string} out of the cart")]
    public Task WhenGuestTriesToTakeOutOfTheCart(string guest, int quantity, string dish) =>
        Actor.AttemptsToAsync(new TakeOutOfTheCart(guest, dish, quantity, mustSucceed: false));

    // ---- looking at the participants ----

    [When("{someone} looks at the participants of the group")]
    [Scope(Tag = "api")]
    public Task WhenSomeoneLooksAtTheParticipants(Someone someone) =>
        Actor.AttemptsToAsync(new LookAtTheParticipants(someone));

    /// <summary>Only the owner has a screen for the group; everyone else is turned away by the service (see the `@api` scenarios).</summary>
    [When("{someone} looks at the participants of the group")]
    [Scope(Tag = "ui")]
    public Task WhenSomeoneOpensTheGroupOrderScreen(Someone someone) =>
        someone == Someone.TheOwner
            ? Actor.AttemptsToAsync(new OpenTheGroupOrder())
            : throw new InvalidOperationException($"Only the owner has a group pre-order screen, not {someone}.");

    [When("the owner looks at the participants of a group that does not exist")]
    [Scope(Tag = "api")]
    public Task WhenTheOwnerLooksAtAGroupThatDoesNotExist() =>
        Actor.AttemptsToAsync(new LookAtTheParticipants(Someone.TheOwner, ofAGroupThatDoesNotExist: true));

    [When("the owner looks at the participants of a group that does not exist")]
    [Scope(Tag = "ui")]
    public Task WhenTheOwnerOpensAGroupThatDoesNotExist() =>
        Actor.AttemptsToAsync(new OpenTheGroupOrder(ofABookingThatDoesNotExist: true));

    [When("the owner refreshes the participants")]
    public Task WhenTheOwnerRefreshesTheParticipants() => Actor.AttemptsToAsync(new RefreshTheParticipants());

    // ---- what the owner sees ----

    [Then("the owner sees the participants:")]
    [Scope(Tag = "api")]
    public async Task ThenTheOwnerIsToldTheParticipants(DataTable participants)
    {
        var expected = ExpectedLines(participants);
        await AssertParticipants(await SettledAsync(onScreen: false, shown => Matches(shown, expected)), expected);
    }

    [Then("the owner sees the participants:")]
    [Scope(Tag = "ui")]
    public async Task ThenTheOwnerSeesTheParticipantsOnTheScreen(DataTable participants)
    {
        var expected = ExpectedLines(participants);
        await AssertParticipants(await SettledAsync(onScreen: true, shown => Matches(shown, expected)), expected);
    }

    /// <summary>The service's total counts every cart, submitted or not.</summary>
    [Then("the group total is {decimal}")]
    [Scope(Tag = "api")]
    public async Task ThenTheGroupTotalIs(decimal total)
    {
        var view = await SettledAsync(onScreen: false, shown => shown.Total == total);

        await Assert.That(view.Total).IsEqualTo(total).Because($"the participants were: {view.Body}");
        await Assert.That(view.Total).IsEqualTo(view.Lines.Sum(l => l.Total))
            .Because($"the group total is the sum of the participants' totals: {view.Body}");
    }

    /// <summary>The screen's grand total is what goes to the restaurant: only the submitted carts.</summary>
    [Then("the grand total of the submitted carts is {decimal}")]
    public async Task ThenTheGrandTotalOfTheSubmittedCartsIs(decimal total)
    {
        var view = await SettledAsync(onScreen: true, shown => shown.Total == total);

        await Assert.That(view.Total).IsEqualTo(total).Because(view.Body);
        await Assert.That(view.Total)
            .IsEqualTo(view.Lines.Where(l => l.Cart == ParticipantLine.Submitted).Sum(l => l.Total))
            .Because($"the grand total is the sum of the submitted carts: {view.Body}");
    }

    [Then("the owner is reminded that {int} participants have not submitted their cart")]
    public async Task ThenTheOwnerIsRemindedAboutUnsubmittedCarts(int count)
    {
        var messages = await Actor.AsksForAsync(new TheGroupOrderMessages());
        await Assert.That(messages.PendingReminder).IsNotNull().Because("no reminder about unsubmitted carts was shown");
        await Assert.That(messages.PendingReminder!).StartsWith($"{count} participants haven't submitted their cart");
    }

    [Then("the owner is told nobody has added any dishes yet")]
    public async Task ThenTheOwnerIsToldNobodyHasAddedAnyDishes() =>
        await Assert.That((await Actor.AsksForAsync(new TheGroupOrderMessages())).NothingChosenYet).IsTrue()
            .Because("the screen did not say that no dishes have been added yet");

    /// <summary>
    /// The owner's list is built from an index that catches up with a guest's change a moment later (DynamoDB global
    /// secondary indexes are eventually consistent), so a list read right after the change can still show the old cart —
    /// on the screen too, until the next Refresh. Like the owner would, this looks again (Refresh on the screen, a new
    /// request through the service) a few times until the list is what the step expects; the step then asserts on the
    /// last answer, so a list that never catches up still fails.
    /// </summary>
    private async Task<ParticipantsView> SettledAsync(bool onScreen, Func<ParticipantsView, bool> isAsExpected)
    {
        const int attempts = 5;

        var view = await ReadAsync(onScreen);
        for (var attempt = 1; attempt < attempts && !isAsExpected(view); attempt++)
        {
            await Actor.AttemptsToAsync(onScreen ? new RefreshTheParticipants() : new LookAtTheParticipants(Someone.TheOwner));
            view = await ReadAsync(onScreen);
        }

        return view;
    }

    private Task<ParticipantsView> ReadAsync(bool onScreen) =>
        onScreen ? Actor.AsksForAsync(new TheParticipantsShown()) : Actor.AsksForAsync(new TheParticipantsListed());

    private static List<ParticipantLine> ExpectedLines(DataTable participants) =>
        participants.Rows.Select(row => new ParticipantLine(
            Who: row["participant"],
            Dishes: row["dishes"] == ParticipantLine.NoDishes ? ParticipantLine.NoDishes : ParticipantLine.Normalize(row["dishes"]),
            Total: ParseAmount(row["total"]),
            Cart: row["cart"])).ToList();

    private static bool Matches(ParticipantsView view, List<ParticipantLine> expected) =>
        view.Lines.Count == expected.Count && expected.All(view.Lines.Contains);

    private static async Task AssertParticipants(ParticipantsView view, List<ParticipantLine> expected)
    {
        await Assert.That(view.Lines.Count).IsEqualTo(expected.Count).Because($"the participants were: {view.Body}");
        await Assert.That(view.Lines[0].Who).IsEqualTo(ParticipantLine.TheOwner)
            .Because($"the owner is always listed first: {view.Body}");

        foreach (var line in expected)
        {
            var actual = view.Lines.FirstOrDefault(l => l.Who == line.Who);
            await Assert.That(actual).IsEqualTo(line).Because($"the participants were: {view.Body}");
        }
    }

    private static decimal ParseAmount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
