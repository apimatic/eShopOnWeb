using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded at the billing provider.
/// </summary>
public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
