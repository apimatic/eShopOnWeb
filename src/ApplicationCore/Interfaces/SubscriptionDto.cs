using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public decimal PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
