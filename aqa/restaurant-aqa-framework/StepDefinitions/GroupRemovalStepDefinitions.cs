using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Removing a participant from the group pre-order, and the second, independent group some scenarios need.
/// After a removal through the service, the updated list is the last response, so "the owner sees the participants"
/// checks it directly; on the screen the card disappears and the totals are recalculated.</summary>
[Binding]
public sealed class GroupRemovalStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    private const string TheOtherGroup = "the other group";

    [Given("another customer has booked a table for {int} guests")]
    public Task GivenAnotherCustomerHasBookedATable(int guests) =>
        Actor.AttemptsToAsync(new BookATableForTheGroup(guests, Personas.AnotherCustomer, TheOtherGroup));

    [Given("{string} has joined the other group")]
    public Task GivenGuestHasJoinedTheOtherGroup(string guest) => Actor.AttemptsToAsync(new JoinTheGroup(guest, TheOtherGroup));

    [Given("the owner has removed {string} from the group")]
    public Task GivenTheOwnerHasRemoved(string guest) =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(Someone.TheOwner, guest, mustSucceed: true));

    [When("{someone} removes {string} from the group")]
    [Scope(Tag = "api")]
    public Task WhenSomeoneRemoves(Someone someone, string guest) =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(someone, guest));

    /// <summary>Only the owner has the screen with the Remove Guest buttons.</summary>
    [When("{someone} removes {string} from the group")]
    [Scope(Tag = "ui")]
    public Task WhenSomeoneRemovesOnTheScreen(Someone someone, string guest) =>
        someone == Someone.TheOwner
            ? Actor.AttemptsToAsync(new RemoveTheParticipantOnTheScreen(guest))
            : throw new InvalidOperationException($"Only the owner has a group pre-order screen, not {someone}.");

    [When("the owner removes a participant who does not exist")]
    public Task WhenTheOwnerRemovesANonExistentParticipant() =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(Someone.TheOwner, removal: Removal.ANonExistentParticipant));

    [When("the owner tries to remove themselves from the group")]
    public Task WhenTheOwnerTriesToRemoveThemselves() =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(Someone.TheOwner, removal: Removal.TheOwner));
}
