using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe attempt. <see cref="AlreadyEnrolled"/> distinguishes a fresh
/// enrollment from an idempotent replay - a double-clicked or retried subscribe returns the
/// subscription that already exists instead of creating a second one.
/// </summary>
public class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(SubscriptionSummary subscription, bool alreadyEnrolled)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        AlreadyEnrolled = alreadyEnrolled;
    }

    public SubscriptionSummary Subscription { get; }

    public bool AlreadyEnrolled { get; }

    public static SubscriptionEnrollmentResult Created(SubscriptionSummary subscription) => new(subscription, false);

    public static SubscriptionEnrollmentResult Existing(SubscriptionSummary subscription) => new(subscription, true);
}
