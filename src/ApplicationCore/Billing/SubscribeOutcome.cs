namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Result of a subscribe attempt. When the customer already had a live subscription
/// to the requested plan, <see cref="AlreadySubscribed"/> is true and no new
/// subscription was created (idempotent outcome).
/// </summary>
public class SubscribeOutcome
{
    public BillingSubscription Subscription { get; init; } = default!;
    public bool AlreadySubscribed { get; init; }
}
