using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a reconciliation entry lines up between PayPal's records and eShop's orders.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal transaction has a matching eShop order.</summary>
    Matched = 0,

    /// <summary>PayPal knows about a transaction eShop has no record of.</summary>
    PayPalOnly = 1,

    /// <summary>eShop captured an order PayPal's report has no transaction for.</summary>
    EShopOnly = 2
}

/// <summary>A single line of the reconciliation report.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    string? InvoiceId,
    int? OrderId,
    decimal? OrderCapturedAmount,
    string? OrderPaymentStatus);

/// <summary>
/// PayPal's own record of transactions for a date range, lined up against eShop orders so a
/// payment one side knows about and the other doesn't is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyCollection<ReconciliationEntry> Entries);
