using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan (a Maxio Advanced Billing Product) offered to shoppers.
/// </summary>
public class SubscriptionPlan
{
    public int ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool HasTrial { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Archived { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
