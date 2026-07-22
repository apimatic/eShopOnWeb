using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A single usage report accepted by the billing provider against a metered component.
/// </summary>
public class BillingUsageRecord
{
    public long Id { get; set; }
    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
