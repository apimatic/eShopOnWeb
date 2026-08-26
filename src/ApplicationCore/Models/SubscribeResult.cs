namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Outcome of a subscribe call. <see cref="Created"/> is false when the shopper already
/// had an active subscription to the plan and the existing one was returned (idempotency).
/// </summary>
public class SubscribeResult
{
    public CustomerSubscriptionDto Subscription { get; set; } = new();
    public bool Created { get; set; }
}
