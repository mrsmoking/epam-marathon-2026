using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>Who is signed in, and where a waiter works. A customer can only book a table at a location that has a
/// waiter (the backend assigns the least busy one), so group scenarios set up their table at the waiter's location.</summary>
public sealed class WaitersApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetMeAsync(string bearerToken) =>
        GetAsync(Url("/me"), new APIRequestContextOptions { Headers = Headers(bearerToken) });

    public Task<IAPIResponse> GetWaiterLocationAsync(string userId, string bearerToken) =>
        GetAsync(Url($"/waiters/{userId}/location"), new APIRequestContextOptions { Headers = Headers(bearerToken) });
}
