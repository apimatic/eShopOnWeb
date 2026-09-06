namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper already had a live subscription to the plan and no new one was created, so
/// callers can answer <c>200 OK</c> instead of <c>201 Created</c>.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
