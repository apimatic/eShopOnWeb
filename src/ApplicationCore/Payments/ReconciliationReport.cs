using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation report over a date range: every PayPal transaction lined up against the eShop
/// payment it belongs to. Rows where one side is missing (<see cref="ReconciliationMatchState"/>)
/// are what an operator needs to investigate.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationRow> Rows { get; init; } = Array.Empty<ReconciliationRow>();

    public int MatchedCount { get; init; }
    public int PayPalOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
}

public enum ReconciliationMatchState
{
    /// <summary>Present on both PayPal and eShop.</summary>
    Matched,
    /// <summary>PayPal knows about the transaction but eShop has no matching payment.</summary>
    PayPalOnly,
    /// <summary>eShop has a payment PayPal's report does not (yet) show.</summary>
    EShopOnly
}

public record ReconciliationRow(
    ReconciliationMatchState State,
    // eShop side
    int? OrderId,
    string? EShopReference,
    string? EShopPaymentStatus,
    decimal? EShopAmount,
    // PayPal side
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    DateTimeOffset? PayPalDate);
