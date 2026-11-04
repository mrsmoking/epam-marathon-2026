using System.Globalization;
using Restaurant_Aqa_Framework.Ui;
using Restaurant_Aqa_Framework.Ui.Components;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The participants on the owner's group pre-order screen, in the same words as <see cref="TheParticipantsListed"/>
/// so one Then step can check either. Each card is expanded to read its dishes; dishes are named by the short names
/// the scenario put on the menu. <see cref="ParticipantsView.Total"/> is the screen's grand total — the submitted carts only.</summary>
public sealed class TheParticipantsShown : IQuestion<ParticipantsView>
{
    public async Task<ParticipantsView> AnsweredByAsync(Actor actor)
    {
        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();

        var aliases = actor.RecallAll<MenuDish>().ToDictionary(d => d.Dish.Name, d => d.Alias);
        var lines = new List<ParticipantLine>();

        for (var i = 0; i < await page.ParticipantCards.CountAsync(); i++)
        {
            var card = page.Participant(i);
            await card.ExpandAsync();

            var title = (await card.Title.InnerTextAsync()).Trim();
            var who = title == ParticipantCardComponent.OwnCardTitle || title.StartsWith("Owner: ", StringComparison.Ordinal)
                ? ParticipantLine.TheOwner
                : title.Replace(ParticipantCardComponent.GuestTitlePrefix, "", StringComparison.Ordinal);

            var dishes = new List<string>();
            foreach (var item in await card.Items.AllAsync())
            {
                var name = (await ParticipantCardComponent.ItemName(item).InnerTextAsync()).Trim();
                var quantity = (await ParticipantCardComponent.ItemQuantity(item).InnerTextAsync()).Replace("×", "", StringComparison.Ordinal).Trim();
                dishes.Add($"{quantity} x {aliases.GetValueOrDefault(name, name)}");
            }

            var chip = (await card.Chip.InnerTextAsync()).Trim();
            lines.Add(new ParticipantLine(
                Who: who,
                Dishes: dishes.Count == 0 ? ParticipantLine.NoDishes : ParticipantLine.Normalize(string.Join(", ", dishes)),
                Total: ParseMoney(await card.Total.InnerTextAsync()),
                Cart: chip == ParticipantCardComponent.SubmittedChip ? ParticipantLine.Submitted : ParticipantLine.NotSubmitted));
        }

        var grandTotal = ParseMoney(await page.GrandTotal.InnerTextAsync());
        var shown = string.Join("; ", lines.Select(l => $"{l.Who}: {l.Dishes}, {l.Total}, {l.Cart}")) + $"; grand total {grandTotal}";
        return new ParticipantsView(lines, grandTotal, $"the screen showed {shown}");
    }

    /// <summary>"$1,234.50" → 1234.50 (the currency pipe's en-US format).</summary>
    private static decimal ParseMoney(string text) =>
        decimal.Parse(new string(text.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray()), CultureInfo.InvariantCulture);
}

/// <summary>What the owner's group pre-order screen tells the owner besides the cards.</summary>
public sealed record GroupOrderMessages(string? PendingReminder, bool NothingChosenYet, bool NotFound);

public sealed class TheGroupOrderMessages : IQuestion<GroupOrderMessages>
{
    public async Task<GroupOrderMessages> AnsweredByAsync(Actor actor)
    {
        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();

        var reminder = await page.PendingReminder.IsVisibleAsync() ? (await page.PendingReminder.InnerTextAsync()).Trim() : null;
        return new GroupOrderMessages(reminder, await page.NothingChosenYet.IsVisibleAsync(), await page.NotFound.IsVisibleAsync());
    }
}
