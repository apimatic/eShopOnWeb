namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Outcome of a subscribe attempt: the subscription plus whether this call created it.
/// A repeat (double-click) request resolves to the existing subscription with
/// <see cref="WasNewlyCreated"/> = false, never to a duplicate.
/// </summary>
public record SubscribeOutcome(BillingSubscription Subscription, bool WasNewlyCreated);
