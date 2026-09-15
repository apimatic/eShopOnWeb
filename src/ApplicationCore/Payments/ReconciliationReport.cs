using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation report over a date range: PayPal's own transactions lined up against eShop orders,
/// so a payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopSettlementCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInPayPal);

/// <summary>One line in a reconciliation report, tying a PayPal transaction to an eShop settlement (or noting one side missing).</summary>
public record ReconciliationEntry(
    string? PayPalTransactionId,
    int? OrderId,
    string Kind,                 // "capture", "refund" or "unknown"
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string? Currency,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate,
    bool AmountMatches);
