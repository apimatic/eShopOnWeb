using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record ReconciliationRequest
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
}

public record ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int TotalPayPalTransactions { get; init; }
    public int TotalLocalPayments { get; init; }
    public List<ReconciliationRow> Rows { get; init; } = new();
}

public record ReconciliationRow
{
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? TransactionDate { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? Amount { get; init; }
    public string? TransactionStatus { get; init; }
    public int? LocalOrderId { get; init; }
    public string? LocalPaymentStatus { get; init; }
    public string MatchStatus { get; init; } = "";
}
