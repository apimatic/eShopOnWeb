using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopperSubscription
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public bool IsCurrent() =>
        State is not "canceled" and not "expired" and not "failed_to_create" and not "trial_ended";
}
