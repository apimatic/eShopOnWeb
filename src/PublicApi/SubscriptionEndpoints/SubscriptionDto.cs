using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal AmountInCents { get; set; }
    public decimal Amount => AmountInCents / 100m;
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextBillingAt { get; set; }
    public int ProductId { get; set; }
}
