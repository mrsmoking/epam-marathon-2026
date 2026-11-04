using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Business-level outcomes shared by several features ("the visitor is told ...", "could not be
/// found"). How an outcome shows up on the wire — which status code, which error body — is decided here,
/// so feature files never mention it.</summary>
[Binding]
public sealed class OutcomeStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Then("the visitor is told {string}")]
    [Scope(Tag = "api")]
    public async Task ThenVisitorIsTold(string message)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Succeeded).IsFalse().Because($"the request unexpectedly succeeded: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).IsEqualTo(message);
    }

    [Then("sign-in is refused")]
    public async Task ThenSignInIsRefused()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("the guest is told that the {item} could not be found")]
    public async Task ThenGuestIsToldNotFound(string item)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Status).IsEqualTo(404).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).Contains("was not found");
    }

    [Then("the location is not created")]
    public async Task ThenLocationIsNotCreated()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsGreaterThanOrEqualTo(400).Because($"the location was created: {outcome.Body}");
    }

    // Group pre-order: the owner-only routes sit behind the Cognito authorizer, which turns away a missing or
    // non-Cognito token (a guest's session token) itself; a signed-in non-owner is refused by the handler.
    [Then("they are told to sign in first")]
    public async Task ThenTheyAreToldToSignInFirst()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("they are told they may not see the participants")]
    public async Task ThenTheyAreToldTheyMayNotSeeTheParticipants()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(403).Because($"response body was: {outcome.Body}");
    }

    [Then("the owner is told the group could not be found")]
    [Scope(Tag = "api")]
    public async Task ThenTheOwnerIsToldTheGroupCouldNotBeFound()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(404).Because($"response body was: {outcome.Body}");
    }

    [Then("the owner is told the group could not be found")]
    [Scope(Tag = "ui")]
    public async Task ThenTheOwnerSeesTheGroupCouldNotBeFound() =>
        await Assert.That((await Actor.AsksForAsync(new TheGroupOrderMessages())).NotFound).IsTrue()
            .Because("the screen did not say the group reservation was not found");

    // Sending the group pre-order: the service's business refusals are 400 with an errorCode in the body
    // ("GroupReservationSubmitted" for a repeat); a signed-in non-owner is 403.
    [Then("the owner is told the pre-order cannot be sent")]
    [Scope(Tag = "api")]
    public async Task ThenTheOwnerIsToldThePreOrderCannotBeSent()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.Body).DoesNotContain("GroupReservationSubmitted").Because($"response body was: {outcome.Body}");
    }

    [Then("the owner is told the pre-order cannot be sent")]
    [Scope(Tag = "ui")]
    public async Task ThenTheOwnerSeesThePreOrderCannotBeSent()
    {
        var standing = await Actor.AsksForAsync(new TheGroupOrderStanding());
        await Assert.That(standing.Error).IsNotNull().Because($"no error was shown: {standing}");
        await Assert.That(standing.StatusBadge).IsEqualTo("Collecting pre-orders").Because($"the screen showed {standing}");
        await Assert.That(standing.Sent).IsFalse().Because($"the screen showed {standing}");
    }

    [Then("the owner is told the pre-order has already been sent")]
    public async Task ThenTheOwnerIsToldThePreOrderHasAlreadyBeenSent()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.Body).Contains("GroupReservationSubmitted").Because($"response body was: {outcome.Body}");
    }

    [Then("they are told they may not send the pre-order")]
    public async Task ThenTheyAreToldTheyMayNotSendThePreOrder()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(403).Because($"response body was: {outcome.Body}");
    }

    // A guest's cart: an empty one cannot be submitted, a submitted one cannot be changed (both 400 from the service).
    [Then("the guest is told the cart is empty")]
    public async Task ThenTheGuestIsToldTheCartIsEmpty()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.Body).Contains("cart is empty").Because($"response body was: {outcome.Body}");
    }

    [Then("the guest is told the submitted cart cannot be changed")]
    public async Task ThenTheGuestIsToldTheSubmittedCartCannotBeChanged()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.Body).Contains("already submitted").Because($"response body was: {outcome.Body}");
    }

    [Then("the information is available")]
    [Then("the menu is shown")]
    public async Task ThenInformationIsAvailable()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Succeeded).IsTrue().Because($"response status {outcome.Status}, body: {outcome.Body}");
    }
}
