using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui.Components;

/// <summary>`app-participant-card` on the owner's group pre-order screen. The title is "My Pre-Order" for the owner's
/// own card and "Guest: &lt;name&gt;" for a guest; the chip says "Submitted" or "Still choosing". The owner's card starts
/// expanded, a guest's collapsed — the toggle ("Show/Hide &lt;name&gt;'s pre-order") reveals the dishes.</summary>
public sealed partial class ParticipantCardComponent(ILocator root)
{
    public const string OwnCardTitle = "My Pre-Order";
    public const string GuestTitlePrefix = "Guest: ";
    public const string SubmittedChip = "Submitted";

    public ILocator Title => root.GetByRole(AriaRole.Heading, new() { Level = 2 });
    public ILocator Chip => root.Locator(".chip");
    public ILocator Total => root.Locator(".summary-total");
    public ILocator Toggle => root.GetByRole(AriaRole.Button, new() { NameRegex = ToggleName() });
    public ILocator Items => root.Locator("li.item");

    public ILocator RemoveButton(string guestName) =>
        root.GetByRole(AriaRole.Button, new() { Name = $"Remove {guestName}", Exact = true });

    public static ILocator ItemName(ILocator item) => item.Locator(".name");
    public static ILocator ItemQuantity(ILocator item) => item.Locator(".qty");

    public async Task ExpandAsync()
    {
        if (await Toggle.GetAttributeAsync("aria-expanded") != "true")
            await Toggle.ClickAsync();
    }

    [GeneratedRegex("^(Show|Hide) .*'s pre-order$")]
    private static partial Regex ToggleName();
}
