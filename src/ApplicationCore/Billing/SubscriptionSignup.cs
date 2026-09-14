namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Intent of a local eShop user to subscribe to a plan offered by the billing provider.
/// </summary>
public class SubscriptionSignup
{
    /// <summary>
    /// eShop (ASP.NET Identity) user id. Used as the stable key that ties the local user to a billing customer.
    /// </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// eShop user email, used to (re)create the billing customer's contact details when needed.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Handle of the plan to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; init; } = string.Empty;
}
