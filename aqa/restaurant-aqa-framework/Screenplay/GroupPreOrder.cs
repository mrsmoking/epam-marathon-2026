using Restaurant_Aqa_Framework.Api.Dtos.Dishes;

namespace Restaurant_Aqa_Framework.Screenplay;

/// <summary>A customer account the scenario signed up and signed in, remembered under the persona's name
/// (<see cref="Personas"/>), so several customers can take part in one scenario.</summary>
public sealed record CustomerSession(string Email, string Password, string IdToken, string AccessToken);

/// <summary>Marks that the browser already carries <paramref name="Persona"/>'s sign-in, so it is set up only once.</summary>
public sealed record StorefrontSignIn(string Persona);

/// <summary>The owner's booking and the group pre-order that comes with it (any booking for two or more guests).</summary>
public sealed record GroupBooking(string ReservationId, string GroupReservationId, string InviteToken, int GuestCount);

/// <summary>A guest who joined the group, remembered under the guest's name. The token authorises the guest's cart.</summary>
public sealed record GuestSession(string Name, string SessionId, string SessionToken);

/// <summary>A dish the scenario put on the menu, remembered under the short name the feature file uses
/// ("Borscht"); the real dish carries the AQA-Sample- prefix.</summary>
public sealed record MenuDish(string Alias, DishResponse Dish);

/// <summary>The location the standing waiter works at — where group scenarios set up their table and dishes.</summary>
public sealed record WaiterLocation(string LocationId);

/// <summary>Names the customer personas are remembered under.</summary>
public static class Personas
{
    public const string Owner = "owner";
    public const string AnotherCustomer = "another customer";
}

/// <summary>Who is making a request to the group pre-order.</summary>
public enum Someone
{
    TheOwner,
    AnotherCustomer,
    TheWaiter,
    AGuestOfTheGroup,
    AVisitorWhoIsNotSignedIn,
}
