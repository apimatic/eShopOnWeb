using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's recurring subscription as reported by the billing system of record.
/// </summary>
public class SubscriptionInfo
{
    public SubscriptionInfo(int id, string state, int customerId, string? customerReference, string planHandle, string planName, int productPriceInCents, DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextAssessmentAt, DateTimeOffset? activatedAt, DateTimeOffset createdAt, string? paymentCollectionMethod)
    {
        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        ProductPriceInCents = productPriceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public int Id { get; }
    public string State { get; }
    public int CustomerId { get; }
    public string? CustomerReference { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public int ProductPriceInCents { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public string? PaymentCollectionMethod { get; }
}
