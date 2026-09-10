namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Result of a subscribe request. <see cref="AlreadyExisted"/> is true when an equivalent live
/// subscription was already present and was returned instead of creating a duplicate (idempotency).
/// </summary>
public record SubscriptionEnrollmentResult
{
    public required SubscriptionSummary Subscription { get; init; }

    public bool AlreadyExisted { get; init; }
}
