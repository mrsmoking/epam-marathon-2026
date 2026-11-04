namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The token <paramref name="someone"/> sends with a request: a customer's or the waiter's Cognito id token,
/// a guest's session token (which the owner-only routes do not accept), or nothing for a visitor.</summary>
public sealed class TheBearerTokenOf(Someone someone) : IQuestion<string?>
{
    public async Task<string?> AnsweredByAsync(Actor actor) => someone switch
    {
        Someone.TheOwner => (await actor.AsksForAsync(new TheCustomerSession(Personas.Owner))).IdToken,
        Someone.AnotherCustomer => (await actor.AsksForAsync(new TheCustomerSession(Personas.AnotherCustomer))).IdToken,
        Someone.TheWaiter => (await actor.AsksForAsync(new TheWaiterSession())).IdToken,
        Someone.AGuestOfTheGroup => actor.Recall<GuestSession>().SessionToken,
        Someone.AVisitorWhoIsNotSignedIn => null,
        _ => throw new InvalidOperationException($"No token is known for '{someone}'."),
    };
}
