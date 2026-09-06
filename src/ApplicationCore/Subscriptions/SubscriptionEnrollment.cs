namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrollment attempt.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="AlreadyExisted">
/// True when the shopper was already enrolled and no new subscription was created — the result of a
/// retry or a double click rather than a fresh signup.
/// </param>
public record SubscriptionEnrollment(Subscription Subscription, bool AlreadyExisted);
