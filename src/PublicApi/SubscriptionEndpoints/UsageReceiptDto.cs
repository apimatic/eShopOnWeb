using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageReceiptDto
{
    public long UsageId { get; set; }

    public int SubscriptionId { get; set; }

    public int ComponentId { get; set; }

    public string ComponentHandle { get; set; }

    public decimal Quantity { get; set; }

    public string Memo { get; set; }

    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>The running unit balance for the current period, when it could be read back.</summary>
    public int? PeriodToDateUnits { get; set; }

    /// <summary>False when the running total could not be read; the usage itself still stands.</summary>
    public bool PeriodToDateAvailable { get; set; }

    /// <summary>Plain-language note that the charge lands on the next renewal invoice.</summary>
    public string BillingNote { get; set; }
}
