using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>Dish lines as a screen shows them, by the short names the scenario put on the menu ("Borscht" → 3).</summary>
public sealed record DishesShown(IReadOnlyDictionary<string, int> Quantities, string Shown);

/// <summary>The consolidated pre-order on the owner's group pre-order screen ("Borscht × 3" lines).</summary>
public sealed partial class TheConsolidatedPreOrderShown : IQuestion<DishesShown>
{
    public async Task<DishesShown> AnsweredByAsync(Actor actor)
    {
        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();

        var aliases = actor.RecallAll<MenuDish>().ToDictionary(d => d.Dish.Name, d => d.Alias);
        var quantities = new Dictionary<string, int>();
        foreach (var text in await page.ConsolidatedItems.AllInnerTextsAsync())
        {
            var match = Line().Match(text.Trim());
            if (!match.Success)
                throw new InvalidOperationException($"Unexpected consolidated pre-order line: '{text}'");

            var name = match.Groups["name"].Value.Trim();
            quantities[aliases.GetValueOrDefault(name, name)] = int.Parse(match.Groups["qty"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return new DishesShown(quantities, $"the screen showed: {string.Join(", ", quantities.Select(q => $"{q.Key} × {q.Value}"))}");
    }

    [GeneratedRegex(@"^(?<name>.+) × (?<qty>\d+)$")]
    private static partial Regex Line();
}

/// <summary>The dishes on the waiter's pre-order page ("Quantity: N" under each dish).</summary>
public sealed class TheWaitersPreOrderShown : IQuestion<DishesShown>
{
    public async Task<DishesShown> AnsweredByAsync(Actor actor)
    {
        var page = new WaiterPreOrderPage(actor.LookingAtThePage());
        await page.Dishes.First.WaitForAsync();

        var aliases = actor.RecallAll<MenuDish>().ToDictionary(d => d.Dish.Name, d => d.Alias);
        var quantities = new Dictionary<string, int>();
        foreach (var dish in await page.Dishes.AllAsync())
        {
            var name = (await WaiterPreOrderPage.DishName(dish).InnerTextAsync()).Trim();
            var quantity = new string((await WaiterPreOrderPage.DishQuantity(dish).InnerTextAsync()).Where(char.IsDigit).ToArray());
            var alias = aliases.GetValueOrDefault(name, name);
            quantities[alias] = quantities.GetValueOrDefault(alias) + int.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        }

        return new DishesShown(quantities, $"the waiter saw: {string.Join(", ", quantities.Select(q => $"{q.Key} × {q.Value}"))}");
    }
}

/// <summary>How the owner's group pre-order screen says where the pre-order stands.</summary>
public sealed record GroupOrderStanding(string StatusBadge, bool CanSubmit, bool Sent, string? Error, string? Success);

public sealed class TheGroupOrderStanding : IQuestion<GroupOrderStanding>
{
    public async Task<GroupOrderStanding> AnsweredByAsync(Actor actor)
    {
        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();

        // A toast follows the service's answer by a moment; give it the chance to appear, but do not require one.
        try
        {
            await page.Toast.Error.Or(page.Toast.Success).First.WaitForAsync(new() { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            // No toast: nothing was submitted on this screen.
        }

        var canSubmit = await page.SubmitButton.IsVisibleAsync() && await page.SubmitButton.IsEnabledAsync();
        var error = await page.Toast.Error.IsVisibleAsync() ? (await page.Toast.Error.InnerTextAsync()).Trim() : null;
        var success = await page.Toast.Success.IsVisibleAsync() ? (await page.Toast.Success.InnerTextAsync()).Trim() : null;

        return new GroupOrderStanding(
            (await page.StatusBadge.InnerTextAsync()).Trim(), canSubmit, await page.SentButton.IsVisibleAsync(), error, success);
    }
}
