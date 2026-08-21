namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/>.
/// <see cref="AlreadyExisted"/> is true when an in-flight or completed subscribe for the same
/// shopper and plan was reused (idempotent double-click).
/// </summary>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
