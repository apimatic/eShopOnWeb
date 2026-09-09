using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingSubscription;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing (the system of record).
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string state, long customerId, string? planHandle,
        string? planName, int priceInCents, string currencyCode, int interval, string? intervalUnit,
        DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextAssessmentAt, DateTimeOffset createdAt,
        string? reference)
    {
        Id = id;
        State = state;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        CurrencyCode = currencyCode;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = createdAt;
        Reference = reference;
    }

    /// <summary>Maxio subscription id.</summary>
    public long Id { get; }

    /// <summary>The Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; }

    /// <summary>Maxio customer id that owns this subscription.</summary>
    public long CustomerId { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    public int PriceInCents { get; }

    public string CurrencyCode { get; }

    public int Interval { get; }

    public string? IntervalUnit { get; }

    /// <summary>
    /// End of the current billing period — i.e. when the next scheduled charge occurs.
    /// This is the "next billing date" surfaced to the shopper.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When Maxio will next attempt to capture payment. Usually tracks the period end.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>The app-provided reference stored on the subscription in Maxio.</summary>
    public string? Reference { get; }

    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
