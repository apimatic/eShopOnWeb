using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic view of a billing-provider subscription. Read fresh from the provider on
/// every call — per §8's stateless-mapping decision, eShopOnWeb persists no local copy; the
/// provider is the system of record.
/// </summary>
public class Subscription
{
    public Subscription(
        int id,
        string productHandle,
        string productName,
        long priceInCents,
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? scheduledCancellationAt)
    {
        Id = id;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Status = status;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        ScheduledCancellationAt = scheduledCancellationAt;
    }

    public int Id { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public SubscriptionStatus Status { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextBillingAt { get; }
    public bool CancelAtEndOfPeriod { get; }
    public DateTimeOffset? ScheduledCancellationAt { get; }

    /// <summary>
    /// Whether this subscription already represents a live relationship with its product, for
    /// duplicate-enrollment detection (UC1) — a subscription in one of these states blocks a
    /// second enrollment in the same product for the same customer.
    /// </summary>
    public bool BlocksReEnrollment =>
        Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue or SubscriptionStatus.OnHold;
}
