using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Ui.Components;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The owner's group pre-order screen, `/reservations/{reservationId}/group-order`. It shows a card per
/// participant, the consolidated pre-order of the submitted carts and its grand total, and while the group is open a
/// Refresh button, a reminder about unsubmitted carts and the Submit button.</summary>
public sealed class GroupOrderPage(IPage page) : BasePage(page)
{
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order", Level = 1 });
    public ILocator StatusBadge => Page.Locator(".status-badge");
    public ILocator ParticipantCards => Page.Locator("ul.participants > li");
    public ILocator GrandTotal => Page.Locator(".grand-total-amount");
    public ILocator PendingReminder => Page.Locator("p.notice");
    public ILocator NothingChosenYet => Page.GetByText("Participants haven't added any dishes yet");
    public ILocator NotFound => Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Group reservation not found" });
    public ILocator LoadFailed => Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Couldn't load the group pre-order" });
    public ILocator RefreshButton => Page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });
    public ILocator SubmitButton => Page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order" });

    public Task NavigateAsync(string reservationId) => NavigateToAsync($"/reservations/{reservationId}/group-order");

    /// <summary>The page has finished loading: either the grand total is shown, or one of the alerts.</summary>
    public Task WaitUntilLoadedAsync() =>
        GrandTotal.Or(NotFound).Or(LoadFailed).First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

    public ParticipantCardComponent Participant(int index) => new(ParticipantCards.Nth(index));

    /// <summary>Clicks Refresh and waits for the new list of participants to arrive.</summary>
    public Task RefreshAsync() =>
        Page.RunAndWaitForResponseAsync(
            () => RefreshButton.ClickAsync(),
            response => response.Request.Method == "GET"
                && response.Url.Contains("/sessions", StringComparison.Ordinal)
                && response.Url.Contains("/guest/groups/", StringComparison.Ordinal));
}
