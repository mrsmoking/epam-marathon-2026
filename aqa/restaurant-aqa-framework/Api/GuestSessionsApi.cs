using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>The guest's side of a group pre-order. No Cognito here: the invite and joining are public, and a cart is
/// authorised by the session token returned on joining (sent as a bearer token, checked by the handler itself).
/// The routes are /guest/sessions/{id}/… as registered in RouteRegistry (docs/ApiRoutes.md still says /guest-sessions).</summary>
public sealed class GuestSessionsApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetInviteAsync(string inviteToken) =>
        GetAsync(Url($"/guest/invites/{inviteToken}"));

    public Task<IAPIResponse> JoinAsync(JoinGroupRequest request) =>
        Api.PostAsync(Url("/guest/sessions"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(),
        });

    public Task<IAPIResponse> GetMenuAsync(string guestSessionId, string sessionToken) =>
        GetAsync(Url($"/guest/sessions/{guestSessionId}/menu"), new APIRequestContextOptions
        {
            Headers = Headers(sessionToken),
        });

    public Task<IAPIResponse> AddDishAsync(string guestSessionId, AddGuestDishRequest request, string sessionToken) =>
        Api.PostAsync(Url($"/guest/sessions/{guestSessionId}/dishes"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(sessionToken),
        });

    public Task<IAPIResponse> RemoveDishAsync(string guestSessionId, RemoveGuestDishRequest request, string sessionToken) =>
        DeleteAsync(Url($"/guest/sessions/{guestSessionId}/dishes"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(sessionToken),
        });

    public Task<IAPIResponse> SubmitCartAsync(string guestSessionId, string sessionToken) =>
        Api.PostAsync(Url($"/guest/sessions/{guestSessionId}/submit"), new APIRequestContextOptions
        {
            Headers = Headers(sessionToken),
        });
}
