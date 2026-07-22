using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Confirmation that a quantity of metered usage was recorded against a subscription.
/// </summary>
public class UsageReceipt
{
    public long Id { get; set; }

    public int SubscriptionId { get; set; }

    public int ComponentId { get; set; }

    public string? ComponentHandle { get; set; }

    public decimal Quantity { get; set; }

    public string? Memo { get; set; }

    public DateTimeOffset? RecordedAt { get; set; }
}
