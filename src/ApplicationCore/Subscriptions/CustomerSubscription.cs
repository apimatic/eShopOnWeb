using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, projected from a Maxio Subscription.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        string state,
        int customerId,
        string? customerReference,
        string? planHandle,
        string? planName,
        long productPriceInCents,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        ProductPriceInCents = productPriceInCents;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
    }

    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; }

    /// <summary>Maxio subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; }

    public int CustomerId { get; }

    public string? CustomerReference { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    public long ProductPriceInCents { get; }

    public decimal ProductPrice => ProductPriceInCents / 100m;

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>
    /// When the next renewal charge will be attempted (Maxio <c>next_assessment_at</c>). This is the
    /// "next billing date" surfaced back to the shopper.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// True when this subscription already existed and was returned instead of creating a new one
    /// (idempotent subscribe). False when it was created by the current request.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
