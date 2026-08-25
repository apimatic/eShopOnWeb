using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int UnmatchedPayPalCount { get; set; }
    public int UnmatchedOrderCount { get; set; }
    public List<ReconciliationRecord> Records { get; set; } = new();
}

public class ReconciliationRecord
{
    public string? PayPalTransactionId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public string? PayPalStatus { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? OrderTotal { get; set; }
    public bool Matched { get; set; }
    public string? Note { get; set; }
}
