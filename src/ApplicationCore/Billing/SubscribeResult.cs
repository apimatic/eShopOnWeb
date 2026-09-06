namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper was already enrolled and no new subscription was created — the case a double-click
/// or a client retry produces.
/// </param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
