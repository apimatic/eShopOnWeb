using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan (Maxio Advanced Billing Product) available for purchase.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceCents { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
