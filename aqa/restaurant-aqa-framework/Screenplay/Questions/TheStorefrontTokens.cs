using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>Both tokens the storefront keeps after a sign-in, for the owner or the standing waiter.</summary>
public sealed record StorefrontTokens(string IdToken, string AccessToken);

public sealed class TheStorefrontTokens(Someone who) : IQuestion<StorefrontTokens>
{
    public async Task<StorefrontTokens> AnsweredByAsync(Actor actor)
    {
        switch (who)
        {
            case Someone.TheOwner:
                var owner = await actor.AsksForAsync(new TheCustomerSession(Personas.Owner));
                return new StorefrontTokens(owner.IdToken, owner.AccessToken);

            case Someone.TheWaiter:
                var (username, password) = Settings.Current.RequireWaiter();
                var response = await new AuthApi(actor.UsingTheApi()).SignInAsync(new SignInRequest(username, password));
                if (!response.Ok)
                    throw new InvalidOperationException($"Waiter sign-in failed with {response.Status}: {await response.TextAsync()}");

                var tokens = await response.ReadAsAsync<SignInResponse>();
                return new StorefrontTokens(tokens.IdToken, tokens.AccessToken);

            default:
                throw new InvalidOperationException($"Only the owner and the waiter use the storefront in these scenarios, not {who}.");
        }
    }
}
