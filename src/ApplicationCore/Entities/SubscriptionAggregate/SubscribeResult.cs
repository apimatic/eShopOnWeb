namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="Created">
/// True when this call actually enrolled the shopper. False when an equivalent subscription already
/// existed and was returned instead - the idempotent path.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool Created);
