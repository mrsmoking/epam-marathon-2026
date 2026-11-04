using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The waiter's view of a booking's pre-order, `/waiter-pre-order`: a dish card per line with "Quantity: N"
/// and Confirm / Cancel buttons. The page takes the booking from the navigation state the reservation card leaves in
/// session storage (`waiterPreOrderData`), so it can be opened directly once that is in place.</summary>
public sealed class WaiterPreOrderPage(IPage page) : BasePage(page)
{
    public const string NavigationDataKey = "waiterPreOrderData";

    public ILocator Title => Page.GetByRole(AriaRole.Heading, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Pre-order of ") });
    public ILocator Dishes => Page.Locator(".preorder-list app-dish-card");

    public static ILocator DishName(ILocator dish) => dish.Locator(".dish-name");
    public static ILocator DishQuantity(ILocator dish) => dish.Locator(".pre-order-quantity");

    public Task NavigateAsync() => NavigateToAsync("/waiter-pre-order");
}
