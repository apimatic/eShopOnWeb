using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
}
