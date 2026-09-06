namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The shopper's subscription, whether it was just created or already existed.</param>
/// <param name="AlreadyExisted">
/// True when the request was a no-op because an equivalent subscription was already on file — the
/// second half of a double-click, or any other replay of the same intent.
/// </param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
