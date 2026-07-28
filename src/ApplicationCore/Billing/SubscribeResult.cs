namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a <see cref="Interfaces.IMaxioBillingService.SubscribeAsync"/> call.
/// </summary>
public class SubscribeResult
{
    /// <summary>The subscription the user now holds for the requested plan.</summary>
    public SubscriptionSummary Subscription { get; init; } = new();

    /// <summary>
    /// True when an equivalent active subscription already existed and was returned as-is
    /// (i.e. this call did not create a new subscription). Enables idempotent, double-click-safe
    /// behavior.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
