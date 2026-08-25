namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Result of a subscribe attempt. <paramref name="AlreadyExisted"/> is true when the
/// shopper already held a live subscription for the plan and no new one was created
/// (idempotent replay of a subscribe request).
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
