using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class Subscription
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}
