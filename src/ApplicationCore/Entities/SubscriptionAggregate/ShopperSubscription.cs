using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class ShopperSubscription
{
    public ShopperSubscription(
        int id,
        string planHandle,
        string planName,
        decimal price,
        string state,
        DateTimeOffset? nextBillingDate,
        bool created)
    {
        Id = id;
        PlanHandle = planHandle;
        PlanName = planName;
        Price = price;
        State = state;
        NextBillingDate = nextBillingDate;
        Created = created;
    }

    public int Id { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public decimal Price { get; }
    public string State { get; }
    public DateTimeOffset? NextBillingDate { get; }
    public bool Created { get; }
}
