using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>One PayPal transaction lined up against the eShop order it belongs to, if any.</summary>
public sealed record ReconciliationRow(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? TransactionTime,
    string? InvoiceId,
    string? CustomField,
    int? MatchedOrderId,
    string MatchStatus);

/// <summary>A payment eShop knows about that PayPal's report does not include.</summary>
public sealed record UnmatchedPayment(
    int OrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds,
    decimal? Amount,
    string? Currency,
    string Reason);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalPayPalTransactions,
    int MatchedTransactions,
    int UnmatchedTransactions,
    IReadOnlyList<ReconciliationRow> Transactions,
    IReadOnlyList<UnmatchedPayment> PaymentsMissingFromPayPal);
