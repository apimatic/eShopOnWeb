using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public decimal Price { get; set; }
}
