using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The owner opens the group pre-order screen of their booking (or of one that does not exist), signed in
/// without going through the login form, and waits until the screen has loaded. Opening it again reloads it.</summary>
public sealed class OpenTheGroupOrder(bool ofABookingThatDoesNotExist = false) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        await actor.AttemptsToAsync(new ArriveSignedIn(Personas.Owner));

        var reservationId = ofABookingThatDoesNotExist
            ? Guid.NewGuid().ToString()
            : actor.Recall<GroupBooking>().ReservationId;
        var page = new GroupOrderPage(actor.LookingAtThePage());

        await page.NavigateAsync(reservationId);
        await page.WaitUntilLoadedAsync();
    }
}

/// <summary>The owner presses Refresh on the group pre-order screen and waits for the new list of participants.</summary>
public sealed class RefreshTheParticipants : ITask
{
    public Task PerformAsAsync(Actor actor) => new GroupOrderPage(actor.LookingAtThePage()).RefreshAsync();
}
