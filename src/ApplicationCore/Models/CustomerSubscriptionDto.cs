using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded at the billing provider.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
