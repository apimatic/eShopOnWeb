using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int TotalPayPalTransactions { get; set; }
    public int UnmatchedCount { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
}

public class ReconciliationRow
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? TransactionStatus { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public string? InitiationDate { get; set; }
    public int? EShopOrderId { get; set; }
    public string? EShopPaymentStatus { get; set; }
    public bool Matched { get; set; }
}
