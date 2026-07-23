using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageReportDto
{
    /// <summary>The billing provider's id for the recorded usage.</summary>
    public long UsageId { get; set; }

    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }

    /// <summary>The quantity that was just recorded.</summary>
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>
    /// The accrued unit balance for the current period, or null when the read-back failed. The usage
    /// still stands in that case — only the running total is unavailable.
    /// </summary>
    public decimal? PeriodToDateTotal { get; set; }

    /// <summary>The price of one unit in major units.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>What the accrued usage will add to the next renewal invoice.</summary>
    public decimal? EstimatedCharge { get; set; }
}
