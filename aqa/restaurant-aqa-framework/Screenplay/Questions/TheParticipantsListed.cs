using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>One participant as the owner sees them, in the words of the feature files: "the owner" or the guest's
/// name, the dishes as "2 x Borscht, 1 x Khachapuri" (sorted, "nothing" when empty), the total and whether the cart
/// is "submitted".</summary>
public sealed record ParticipantLine(string Who, string Dishes, decimal Total, string Cart)
{
    public const string TheOwner = "the owner";
    public const string NoDishes = "nothing";
    public const string Submitted = "submitted";
    public const string NotSubmitted = "not submitted";

    /// <summary>The same dishes written in any order compare equal.</summary>
    public static string Normalize(string dishes) =>
        string.Join(", ", dishes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal));
}

public sealed record ParticipantsView(IReadOnlyList<ParticipantLine> Lines, decimal Total, string Body);

/// <summary>The participants in the scenario's last response (the owner's list, or the snapshot after removing a
/// guest). Dishes are named by the short names the scenario put on the menu.</summary>
public sealed class TheParticipantsListed : IQuestion<ParticipantsView>
{
    public Task<ParticipantsView> AnsweredByAsync(Actor actor)
    {
        var response = actor.TheLastResponse();
        var group = response.Json<GroupParticipantsResponse>();
        var aliases = actor.RecallAll<MenuDish>().ToDictionary(d => d.Dish.Id, d => d.Alias);

        var lines = group.Participants.Select(p => new ParticipantLine(
            Who: p.IsOwner ? ParticipantLine.TheOwner : p.Name,
            Dishes: p.Items.Count == 0
                ? ParticipantLine.NoDishes
                : ParticipantLine.Normalize(string.Join(", ", p.Items.Select(i =>
                    $"{i.Quantity} x {aliases.GetValueOrDefault(i.Dish.Id, i.Dish.Name)}"))),
            Total: p.Total,
            Cart: p.IsSubmitted ? ParticipantLine.Submitted : ParticipantLine.NotSubmitted)).ToList();

        return Task.FromResult(new ParticipantsView(lines, group.Total, response.Body));
    }
}
