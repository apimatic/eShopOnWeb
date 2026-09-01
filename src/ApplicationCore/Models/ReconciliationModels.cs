using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<ReconciliationTransaction> Transactions,
    IReadOnlyList<ReconciliationLocalPayment> MissingFromPayPal)
{
    /// <summary>PayPal transactions that could not be lined up with any eShop order.</summary>
    public IReadOnlyList<ReconciliationTransaction> UnmatchedTransactions =>
        Transactions.Where(t => t.MatchedOrderId is null).ToList();
}

public sealed record ReconciliationTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    string? InvoiceId,
    string? CustomField,
    string? PayPalReferenceId,
    int? MatchedOrderId);

public sealed record ReconciliationLocalPayment(
    int OrderId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedAmount,
    string? Currency,
    string Reason);
