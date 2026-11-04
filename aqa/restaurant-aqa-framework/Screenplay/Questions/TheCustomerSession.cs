using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The customer playing <paramref name="persona"/> in this scenario — a fresh AQA-Sample- account, signed up
/// and signed in the first time it is asked for, then remembered. Accounts cannot be deleted without an admin token;
/// the run's sweep finds them by their prefix.</summary>
public sealed class TheCustomerSession(string persona) : IQuestion<CustomerSession>
{
    public async Task<CustomerSession> AnsweredByAsync(Actor actor)
    {
        if (actor.TryRecall<CustomerSession>(persona, out var session))
            return session;

        var auth = new AuthApi(actor.UsingTheApi());
        var registration = SampleDataFactory.RegisterRequest();

        var registered = await auth.RegisterAsync(registration);
        if (!registered.Ok)
            throw new InvalidOperationException(
                $"Signing up {persona} failed with {registered.Status}: {await registered.ReadErrorMessageAsync()}");

        var signedIn = await auth.SignInAsync(new SignInRequest(registration.Email, registration.Password));
        if (!signedIn.Ok)
            throw new InvalidOperationException(
                $"Signing in {persona} failed with {signedIn.Status}: {await signedIn.ReadErrorMessageAsync()}");

        var tokens = await signedIn.ReadAsAsync<SignInResponse>();
        session = new CustomerSession(registration.Email, registration.Password, tokens.IdToken, tokens.AccessToken);
        actor.Remember(persona, session);
        return session;
    }
}
