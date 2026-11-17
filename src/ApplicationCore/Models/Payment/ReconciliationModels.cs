using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payment;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int EshopTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationEntry
{
    public string Source { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? Currency { get; set; }
    public long? AmountCents { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public int? OrderId { get; set; }
    public int? RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public string? Note { get; set; }
}
