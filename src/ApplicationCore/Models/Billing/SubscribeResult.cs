namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// Outcome of a subscribe operation. <see cref="CreatedNew"/> is false when the caller
/// already held a live subscription to the requested plan (idempotent re-subscribe).
/// </summary>
/// <param name="Subscription">The subscription in the billing system of record.</param>
/// <param name="CreatedNew">True when this call created the subscription, false when an existing live one was returned.</param>
public sealed record SubscribeResult(SubscriptionDetails Subscription, bool CreatedNew);
