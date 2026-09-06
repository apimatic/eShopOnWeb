namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrolment attempt.
/// </summary>
/// <param name="Subscription">The live subscription the shopper now holds.</param>
/// <param name="AlreadySubscribed">
/// True when the request was a no-op because an equivalent subscription already existed -
/// the double-click / retry case. The subscription returned is the pre-existing one.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
