using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// An application-level view of a customer's subscription, projected from a Maxio subscription.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id, string state, string? planHandle, string? planName,
        long priceInCents, string formattedPrice, int interval, string? intervalUnit,
        DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextAssessmentAt, DateTimeOffset? createdAt,
        string? paymentCollectionMethod)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        FormattedPrice = formattedPrice;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = createdAt;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; }
    /// <summary>The subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; }
    public string? PlanHandle { get; }
    public string? PlanName { get; }
    public long PriceInCents { get; }
    public string FormattedPrice { get; }
    public int Interval { get; }
    public string? IntervalUnit { get; }
    /// <summary>End of the current billing period, i.e. the next billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset? CreatedAt { get; }
    public string? PaymentCollectionMethod { get; }
}
