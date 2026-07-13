using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as read from the billing provider. eShopOnWeb keeps no local copy of this
/// state (persistence is stateless, §8) — the provider is always the source of truth.
/// </summary>
public class BillingSubscription
{
    public BillingSubscription(
        long id,
        long customerId,
        string customerReference,
        long productId,
        string productHandle,
        string productName,
        long productPriceInCents,
        SubscriptionLifecycleState state,
        long balanceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt)
    {
        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductId = productId;
        ProductHandle = productHandle;
        ProductName = productName;
        ProductPriceInCents = productPriceInCents;
        State = state;
        BalanceInCents = balanceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
    }

    public long Id { get; }
    public long CustomerId { get; }
    public string CustomerReference { get; }
    public long ProductId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long ProductPriceInCents { get; }
    public SubscriptionLifecycleState State { get; }
    public long BalanceInCents { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
}
