using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a PayPal transaction lines up (or fails to) against an eShop order/payment.</summary>
public enum ReconciliationOutcome
{
    /// <summary>PayPal transaction matched to an eShop order.</summary>
    Matched,

    /// <summary>PayPal knows about the transaction but eShop has no matching order.</summary>
    InPayPalOnly,

    /// <summary>eShop captured a payment PayPal's report does not (yet) show.</summary>
    InEShopOnly,
}

/// <summary>One reconciled row.</summary>
public record ReconciliationEntry(
    ReconciliationOutcome Outcome,
    string? PayPalTransactionId,
    string? EventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? CurrencyCode,
    DateTimeOffset? TransactionDate,
    string? InvoiceId,
    int? OrderId,
    string? EShopCaptureId,
    decimal? EShopCapturedAmount,
    string? EShopStatus);

/// <summary>Reconciliation report over a requested date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
