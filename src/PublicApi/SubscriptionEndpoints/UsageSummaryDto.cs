using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageSummaryDto
{
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; } = string.Empty;

    /// <summary>False when the running total could not be read back; the recorded usage still stands.</summary>
    public bool TotalAvailable { get; set; }

    public decimal PeriodToDateQuantity { get; set; }
    public decimal? UnitPrice { get; set; }

    /// <summary>What the accrued usage will add to the next renewal invoice.</summary>
    public decimal? EstimatedCharge { get; set; }

    public DateTimeOffset? PeriodStartedAt { get; set; }
    public DateTimeOffset? NextInvoiceAt { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class UsageRecordDto
{
    public long Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
