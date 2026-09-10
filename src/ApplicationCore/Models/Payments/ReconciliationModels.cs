using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// A reconciliation report over a date range: PayPal's own transaction records lined up against eShop orders,
/// so a payment that only one side knows about is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopOrderCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalOnly,
    IReadOnlyList<ReconciliationEShopOnly> InEShopOnly);

/// <summary>A PayPal transaction that lines up with an eShop order, with a flag for whether the amounts agree.</summary>
public record ReconciliationMatch(
    int OrderId,
    string TransactionId,
    string PayPalStatus,
    decimal PayPalAmount,
    string EShopStatus,
    decimal OrderTotal,
    bool AmountsAgree);

/// <summary>A PayPal transaction with no matching eShop order in this range.</summary>
public record ReconciliationPayPalOnly(
    string TransactionId,
    string? InvoiceId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset InitiationDate);

/// <summary>An eShop order with payment activity that PayPal's report does not (yet) show in this range.</summary>
public record ReconciliationEShopOnly(
    int OrderId,
    string EShopStatus,
    decimal OrderTotal,
    string? PayPalOrderId,
    string? CaptureId);
