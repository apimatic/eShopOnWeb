using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionSummary
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
