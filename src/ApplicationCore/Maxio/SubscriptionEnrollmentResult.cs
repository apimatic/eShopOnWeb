namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Result of an idempotent subscribe operation. <see cref="AlreadyExisted"/> is true when an
/// equivalent, still-current subscription was found instead of a new one being created - this
/// is what makes a double-click safe.
/// </summary>
public record SubscriptionEnrollmentResult(MaxioSubscription Subscription, bool AlreadyExisted);
