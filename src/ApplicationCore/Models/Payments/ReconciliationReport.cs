using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
    public int MatchedCount { get; set; }
    public int UnknownToEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
}

public class ReconciliationRow
{
    /// <summary>Matched, UnknownToEShop or MissingInPayPal.</summary>
    public string MatchStatus { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Note { get; set; }
}
