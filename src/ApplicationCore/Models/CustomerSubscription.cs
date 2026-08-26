using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in Maxio.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public string State { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
