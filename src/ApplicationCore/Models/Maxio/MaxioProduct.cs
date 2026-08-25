using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// Application-facing view of a Maxio Advanced Billing product (a subscription plan).
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
}
