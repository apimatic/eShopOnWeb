namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Outcome of <see cref="Interfaces.ISubscriptionService.SubscribeAsync"/>.</summary>
/// <param name="Subscription">The live subscription the shopper now holds.</param>
/// <param name="AlreadySubscribed">
/// True when the request resolved to a subscription that already existed - a repeated submit,
/// a double-click, or a retry - and nothing new was created in the billing system.
/// </param>
/// <param name="CustomerCreated">True when a Maxio customer had to be created for this shopper.</param>
public record SubscribeResult(Subscription Subscription, bool AlreadySubscribed, bool CustomerCreated);
