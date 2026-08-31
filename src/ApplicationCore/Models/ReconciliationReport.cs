using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop orders.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<UnmatchedPayment> PaymentsMissingFromPayPal);

public sealed record ReconciliationEntry(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    int? OrderId,
    int? PaymentId,
    string Match);

public sealed record UnmatchedPayment(
    int PaymentId,
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds);
