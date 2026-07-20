using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class BillingSubscription
{
    public BillingSubscription(
        int id,
        int customerId,
        string? customerReference,
        int? productId,
        string? productHandle,
        long? priceInCents,
        SubscriptionLifecycleState state,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? currentPeriodEndsAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        string? nextProductHandle)
    {
        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductId = productId;
        ProductHandle = productHandle;
        PriceInCents = priceInCents;
        State = state;
        NextAssessmentAt = nextAssessmentAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        NextProductHandle = nextProductHandle;
    }

    public int Id { get; }
    public int CustomerId { get; }
    public string? CustomerReference { get; }
    public int? ProductId { get; }
    public string? ProductHandle { get; }
    public long? PriceInCents { get; }
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;
    public SubscriptionLifecycleState State { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public bool CancelAtEndOfPeriod { get; }
    public DateTimeOffset? DelayedCancelAt { get; }

    /// <summary>Populated once a delayed (at-renewal) plan change has been scheduled.</summary>
    public string? NextProductHandle { get; }

    public bool IsLive => State != SubscriptionLifecycleState.Canceled && State != SubscriptionLifecycleState.Expired;
}
