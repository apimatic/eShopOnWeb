using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public class CustomerSubscription
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
