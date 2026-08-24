namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Result of a subscribe attempt. <see cref="AlreadyExisted"/> is true when the shopper already
/// had a live subscription to the plan and the existing one was returned instead of creating a duplicate.
/// </summary>
public record SubscribeResult(SubscriptionDetails Subscription, bool AlreadyExisted);
