using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>The owner's side of a group pre-order: the invite, the participants, removing one and submitting the
/// whole group. Every route sits behind the Cognito authorizer and checks that the caller owns the reservation.</summary>
public sealed class GroupPreOrdersApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetInviteTokenAsync(string reservationId, string? bearerToken = null) =>
        GetAsync(Url($"/reservations/{reservationId}/invite-token"), new APIRequestContextOptions
        {
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> GetOwnerSessionIdAsync(string reservationId, string? bearerToken = null) =>
        GetAsync(Url($"/reservations/{reservationId}/guest-session-id"), new APIRequestContextOptions
        {
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> GetParticipantsAsync(string groupReservationId, string? bearerToken = null) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/sessions"), new APIRequestContextOptions
        {
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> RemoveParticipantAsync(
        string groupReservationId, string guestSessionId, string? bearerToken = null) =>
        DeleteAsync(Url($"/guest/groups/{groupReservationId}/sessions/{guestSessionId}"), new APIRequestContextOptions
        {
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> SubmitAsync(string groupReservationId, string reservationId, string? bearerToken = null) =>
        Api.PostAsync(Url($"/group-reservations/{groupReservationId}/submit"), new APIRequestContextOptions
        {
            DataString = Serialize(new SubmitGroupPreOrderRequest(reservationId)),
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> GetStatusAsync(string groupReservationId) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/status"));
}
