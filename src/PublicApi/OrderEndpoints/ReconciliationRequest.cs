using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public List<ReconciliationRow> Rows { get; set; } = new();
    public int PayPalTransactionCount { get; set; }
    public int UnmatchedPayPalCount { get; set; }
    public int UnmatchedEShopCount { get; set; }
}

public class ReconciliationRow
{
    public int? EShopOrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InitiationDate { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}
