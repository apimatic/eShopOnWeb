using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as reflected by the billing provider, mapped to eShopOnWeb domain terms. Maxio Advanced
/// Billing is the system of record (decided stateless per plan.md §8), so this is always constructed fresh
/// from a provider response rather than persisted locally.
/// </summary>
public class Subscription
{
    public Subscription(
        int id,
        int customerId,
        string customerReference,
        string productHandle,
        string productName,
        long priceInCents,
        string state,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? scheduledCancellationAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        ScheduledCancellationAt = scheduledCancellationAt;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
    }

    public int Id { get; }
    public int CustomerId { get; }
    public string CustomerReference { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }

    /// <summary>The provider's raw subscription state wire value (e.g. "active", "on_hold", "canceled").</summary>
    public string State { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public bool CancelAtEndOfPeriod { get; }
    public DateTimeOffset? ScheduledCancellationAt { get; }
    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CreatedAt { get; }

    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);
    public bool IsPaused => string.Equals(State, "on_hold", StringComparison.OrdinalIgnoreCase);
    public bool IsCanceled => string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase);
}
