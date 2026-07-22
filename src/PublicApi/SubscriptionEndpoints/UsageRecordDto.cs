using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A single accepted usage report.
/// </summary>
public class UsageRecordDto
{
    public long Id { get; set; }
    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}
