using System.Text.Json;
using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.Abilities;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Opens the storefront already signed in as <paramref name="persona"/>: the tokens and the profile the
/// login form would have stored are put into the browser's local storage before any page loads. For scenarios where
/// signing in is not what is being tested — the password never reaches the page, so it never lands in a trace.</summary>
public sealed class ArriveSignedIn(string persona) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        if (actor.TryRecall<StorefrontSignIn>(persona, out _))
            return;

        var session = await actor.AsksForAsync(new TheCustomerSession(persona));
        var me = await new WaitersApi(actor.UsingTheApi()).GetMeAsync(session.IdToken);
        if (!me.Ok)
            throw new InvalidOperationException($"Reading the profile of {persona} failed with {me.Status}: {await me.TextAsync()}");

        // The same keys AuthenticationService writes after a successful sign-in: 'auth' (tokens) and 'user' (GET /me).
        var auth = JsonSerializer.Serialize(new { idToken = session.IdToken, accessToken = session.AccessToken });
        var user = await me.TextAsync();
        var script =
            $"window.localStorage.setItem('auth', {JsonSerializer.Serialize(auth)});" +
            $"window.localStorage.setItem('user', {JsonSerializer.Serialize(user)});";

        await actor.AbilityTo<IBrowseTheWeb>().Context.AddInitScriptAsync(script);
        actor.Remember(persona, new StorefrontSignIn(persona));
    }
}
