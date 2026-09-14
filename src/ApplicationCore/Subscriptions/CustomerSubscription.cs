using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record. Billing-system-agnostic projection of a Maxio subscription.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        string state,
        string planHandle,
        string planName,
        long productPriceInCents,
        string currency,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        ProductPriceInCents = productPriceInCents;
        Currency = currency;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
    }

    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; }

    /// <summary>The subscription lifecycle state (e.g. <c>active</c>).</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long ProductPriceInCents { get; }

    public decimal Price => ProductPriceInCents / 100m;

    public string Currency { get; }

    /// <summary>
    /// When the current billing period ends and the next charge is scheduled — i.e. the
    /// next billing date shown back to the shopper.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When payment capture will next be attempted. Usually tracks the period end.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CreatedAt { get; }
}
