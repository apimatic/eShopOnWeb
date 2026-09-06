namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="AlreadyExisted">
/// True when the shopper was already enrolled on the plan and no new subscription was created.
/// This is what makes a double-click safe.
/// </param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
