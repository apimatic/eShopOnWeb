using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public class SubscriptionDetails
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string? State { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public bool AlreadyExisted { get; init; }
}
