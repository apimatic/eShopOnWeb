using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The reconciliation state of a single line in the report.</summary>
public enum ReconciliationState
{
    /// <summary>Present in both PayPal's records and eShop's.</summary>
    Matched = 0,

    /// <summary>PayPal knows about the transaction but eShop has no record of it.</summary>
    PayPalOnly = 1,

    /// <summary>eShop recorded the transaction but PayPal's report does not list it.</summary>
    EShopOnly = 2
}

/// <summary>
/// One reconciled transaction: PayPal's view lined up against eShop's, so a payment one side knows
/// about and the other does not is visible.
/// </summary>
public record ReconciliationLine(
    string TransactionId,
    ReconciliationState State,
    int? OrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? PayPalStatus,
    string Kind,
    string CurrencyCode);

/// <summary>
/// A reconciliation report over a date range: PayPal's own record of transactions lined up against
/// eShop orders. Covers the whole range (every page of PayPal's report), not just the first page.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
