using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's subscription as confirmed by the billing system of record (Maxio).
/// </summary>
public sealed class ShopperSubscription
{
    public ShopperSubscription(
        int id,
        string planHandle,
        string planName,
        int priceInCents,
        string state,
        DateTimeOffset? nextBillingDate)
    {
        Id = id;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingDate = nextBillingDate;
    }

    public int Id { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public int PriceInCents { get; }
    public string State { get; }
    public DateTimeOffset? NextBillingDate { get; }
    public decimal Price => PriceInCents / 100m;
}
