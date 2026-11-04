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
    public ILocator SentButton => Page.GetByRole(AriaRole.Button, new() { Name = "Pre-Order Sent" });
    public ILocator ConfirmSubmitButton => Page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true });

    /// <summary>"Borscht × 3" lines of the consolidated pre-order (the submitted carts only).</summary>
    public ILocator ConsolidatedItems => Page.Locator(".consolidated-item > span:first-child");
    public ToastComponent Toast { get; } = new(page);

    /// <summary>Presses Submit Pre-Order, confirms in the dialog and waits for the service's answer.</summary>
    public async Task SubmitAsync()
    {
        await SubmitButton.ClickAsync();
        await Page.RunAndWaitForResponseAsync(
            () => ConfirmSubmitButton.ClickAsync(),
            response => response.Request.Method == "POST"
                && response.Url.Contains("/group-reservations/", StringComparison.Ordinal)
                && response.Url.EndsWith("/submit", StringComparison.Ordinal));
    }

    public Task NavigateAsync(string reservationId) => NavigateToAsync($"/reservations/{reservationId}/group-order");

    /// <summary>The page has finished loading: either the grand total is shown, or one of the alerts.</summary>
    public Task WaitUntilLoadedAsync() =>
        GrandTotal.Or(NotFound).Or(LoadFailed).First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

    public ParticipantCardComponent Participant(int index) => new(ParticipantCards.Nth(index));

    public ParticipantCardComponent Guest(string name) =>
        new(ParticipantCards.Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = $"{ParticipantCardComponent.GuestTitlePrefix}{name}", Exact = true }),
        }));

    /// <summary>The dialog's own "Remove Guest" button — the card's button of the same text is named "Remove &lt;name&gt;".</summary>
    public ILocator ConfirmRemoveButton => Page.GetByRole(AriaRole.Button, new() { Name = "Remove Guest", Exact = true });

    /// <summary>Presses Remove on the guest's card, confirms in the dialog and waits for the updated list.</summary>
    public async Task RemoveGuestAsync(string name)
    {
        await Guest(name).RemoveButton(name).ClickAsync();
        await Page.RunAndWaitForResponseAsync(
            () => ConfirmRemoveButton.ClickAsync(),
            response => response.Request.Method == "DELETE"
                && response.Url.Contains("/guest/groups/", StringComparison.Ordinal));
        await ConfirmRemoveButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    /// <summary>Clicks Refresh and waits until the list its own request brought back is on the screen: the request is
    /// the one the click sent (not a background sync already under way), and the button is enabled again once the
    /// page has applied the answer.</summary>
    public async Task RefreshAsync()
    {
        var request = await Page.RunAndWaitForRequestAsync(
            () => RefreshButton.ClickAsync(),
            request => request.Method == "GET"
                && request.Url.Contains("/guest/groups/", StringComparison.Ordinal)
                && request.Url.EndsWith("/sessions", StringComparison.Ordinal));
        await request.ResponseAsync();
        await RefreshButton.And(Page.Locator("button:enabled")).WaitForAsync();
    }
}
