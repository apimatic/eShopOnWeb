using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public decimal CurrentPrice { get; set; }
    public string PriceFormatted => $"${CurrentPrice:F2}/{BillingPeriodUnit}";
    public DateTime? NextBillingAt { get; set; }
    public string? NextBillingAtFormatted => NextBillingAt?.ToString("yyyy-MM-dd");
    public int BillingPeriodLength { get; set; }
    public string BillingPeriodUnit { get; set; } = null!;
}
