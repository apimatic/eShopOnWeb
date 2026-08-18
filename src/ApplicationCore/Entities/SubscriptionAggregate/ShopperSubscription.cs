using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb shopper.
/// </summary>
public class ShopperSubscription
{
    public ShopperSubscription(
        int id,
        string planHandle,
        string planName,
        decimal price,
        string state,
        DateTimeOffset? nextBillingDate)
    {
        Id = id;
        PlanHandle = planHandle;
        PlanName = planName;
        Price = price;
        State = state;
        NextBillingDate = nextBillingDate;
    }

    public int Id { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public decimal Price { get; }
    public string State { get; }
    public DateTimeOffset? NextBillingDate { get; }
}
