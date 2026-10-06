using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring subscription owned by a shopper, as recorded in Maxio Advanced Billing.
/// </summary>
public class Subscription
{
    public Subscription(int id, string state, string planHandle, string planName, decimal price,
        DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextAssessmentAt, DateTimeOffset createdAt,
        DateTimeOffset? activatedAt, DateTimeOffset? canceledAt, string paymentCollectionMethod)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        Price = price;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = createdAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public int Id { get; }
    public string State { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public decimal Price { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CanceledAt { get; }
    public string PaymentCollectionMethod { get; }
}
