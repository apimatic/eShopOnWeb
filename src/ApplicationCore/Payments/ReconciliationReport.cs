using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a PayPal transaction and an eShop order line up (or fail to).</summary>
public enum ReconciliationStatus
{
    /// <summary>Present in both PayPal's records and eShop, matched by invoice id.</summary>
    Matched = 0,

    /// <summary>PayPal knows about it but eShop has no payment with that invoice id.</summary>
    PayPalOnly = 1,

    /// <summary>eShop has a payment but PayPal's records (for this range) don't show it.</summary>
    EShopOnly = 2
}

/// <summary>A single reconciled line between PayPal and eShop.</summary>
public record ReconciliationEntry(
    ReconciliationStatus Status,
    string? InvoiceId,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? Currency,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopPaymentStatus,
    decimal? EShopAmount);

/// <summary>
/// Report lining PayPal's own transaction records for a date range up against eShop orders,
/// so anything PayPal knows and eShop doesn't (or the reverse) is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
