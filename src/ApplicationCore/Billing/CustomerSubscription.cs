using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class CustomerSubscription
{
    public int Id { get; init; }
    public required string State { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
