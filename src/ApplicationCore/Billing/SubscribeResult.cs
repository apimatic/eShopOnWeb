namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Result of a subscribe request. <see cref="IsNewSubscription"/> is false when an equivalent,
/// still-live subscription already existed and was returned instead of creating a duplicate.
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool IsNewSubscription);
