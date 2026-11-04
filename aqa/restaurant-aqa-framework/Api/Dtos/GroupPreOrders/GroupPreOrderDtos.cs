namespace Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrders;

/// <summary>GET /reservations/{reservationId}/invite-token — the token the owner shares with the guests.</summary>
public sealed record InviteTokenResponse(string Token);

/// <summary>GET /guest/invites/{token} — what a guest sees before joining; the only way from an invite to its group.</summary>
public sealed record InviteResponse(string GroupReservationId, string RestaurantName, string LocationAddress, DateTime StartTime);

/// <summary>GET /reservations/{reservationId}/guest-session-id — the owner's own session in the group.</summary>
public sealed record OwnerSessionResponse(string GuestSessionId);

/// <summary>POST /guest/sessions.</summary>
public sealed record JoinGroupRequest(string GroupReservationId, string GuestName);

/// <summary>POST /guest/sessions — the guest's session and the token that authorises its cart calls.</summary>
public sealed record JoinGroupResponse(string SessionId, string SessionToken);

/// <summary>POST /guest/sessions/{guestSessionId}/dishes.</summary>
public sealed record AddGuestDishRequest(string DishId, int Quantity);

/// <summary>DELETE /guest/sessions/{guestSessionId}/dishes — how many portions to take out. The handler requires a
/// positive <paramref name="Quantity"/> (docs/ApiRoutes.md shows only dishId).</summary>
public sealed record RemoveGuestDishRequest(string DishId, int Quantity);

/// <summary>POST /group-reservations/{groupReservationId}/submit.</summary>
public sealed record SubmitGroupPreOrderRequest(string ReservationId);

/// <summary>One line of the consolidated pre-order returned by the group submit.</summary>
public sealed record ConsolidatedItem(string DishId, int Quantity);

/// <summary>GET /guest/groups/{groupReservationId}/sessions (and the snapshot returned by DELETE of a session).</summary>
public sealed record GroupParticipantsResponse(IReadOnlyList<Participant> Participants, decimal Total);

/// <summary><paramref name="Type"/> is "Owner" for the reservation owner (always listed first) and "Guest" otherwise.</summary>
public sealed record Participant(
    string Id,
    string Type,
    string Name,
    IReadOnlyList<ParticipantItem> Items,
    decimal Total,
    bool IsSubmitted)
{
    public bool IsOwner => string.Equals(Type, "Owner", StringComparison.OrdinalIgnoreCase);
}

public sealed record ParticipantItem(ParticipantDish Dish, int Quantity);

public sealed record ParticipantDish(string Id, string Name, decimal Price, string State);

/// <summary>GET /guest/groups/{groupReservationId}/status — status as a string ("Open", "Submitted", "Closed").
/// The shape follows GroupReservationStatusResponse in the backend (docs/ApiRoutes.md lists other fields).</summary>
public sealed record GroupStatusResponse(int CurrentParticipants, int MaxCapacity, string Status);
