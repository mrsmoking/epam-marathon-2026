using System.Text.Json;
using Restaurant_Aqa_Framework.Screenplay.Abilities;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The waiter opens the pre-order of the scenario's booking. On the waiter's dashboard the reservation card
/// stores the booking in session storage and navigates to the pre-order page; this does the same directly, so the
/// scenario does not depend on the dashboard's date and time filters. Waits until the dishes have loaded.</summary>
public sealed class OpenTheWaitersPreOrder : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        await actor.AttemptsToAsync(new ArriveSignedIn(Someone.TheWaiter));

        var booking = actor.Recall<GroupBooking>();
        var navigation = JsonSerializer.Serialize(new
        {
            reservationId = booking.ReservationId,
            createdByName = "AQA owner",
            createdByRole = "Customer",
            tableName = "AQA table",
        });
        await actor.AbilityTo<IBrowseTheWeb>().Context.AddInitScriptAsync(
            $"window.sessionStorage.setItem('{WaiterPreOrderPage.NavigationDataKey}', {JsonSerializer.Serialize(navigation)});");

        var page = new WaiterPreOrderPage(actor.LookingAtThePage());
        await page.NavigateAsync();
        await page.Dishes.First.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
    }
}
