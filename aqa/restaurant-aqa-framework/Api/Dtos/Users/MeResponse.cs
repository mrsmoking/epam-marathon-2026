namespace Restaurant_Aqa_Framework.Api.Dtos.Users;

/// <summary>GET /me — the signed-in user's own profile.</summary>
public sealed record MeResponse(string Id, string FirstName, string LastName, string Email, string Role);

/// <summary>GET /waiters/{userId}/location.</summary>
public sealed record WaiterLocationResponse(string LocationId);
