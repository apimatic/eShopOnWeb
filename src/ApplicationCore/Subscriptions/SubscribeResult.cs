namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrollment attempt.
/// </summary>
/// <param name="Subscription">The live subscription, whether it was created now or already existed.</param>
/// <param name="Created">
/// <c>true</c> when this call created the subscription, <c>false</c> when an equivalent enrollment
/// already existed and was returned unchanged.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool Created);
