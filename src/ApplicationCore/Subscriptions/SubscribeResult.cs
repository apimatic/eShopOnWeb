namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The resulting subscription, freshly created or pre-existing.</param>
/// <param name="Created">False when the request was de-duplicated onto an existing subscription.</param>
/// <param name="CustomerCreated">True when a billing customer had to be created for this shopper.</param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool Created, bool CustomerCreated);
