using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class CustomerSubscription
{
    public long? Id { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public long? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
}
