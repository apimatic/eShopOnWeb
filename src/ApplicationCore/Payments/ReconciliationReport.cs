using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Lines PayPal's own record of transactions for a date range up against eShop's orders, so that a
/// payment PayPal knows about but eShop doesn't (or the reverse) is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Total transactions PayPal reported across the whole range.</summary>
    public int PayPalTransactionCount { get; set; }

    /// <summary>Transactions present on both sides, matched by PayPal id.</summary>
    public List<ReconciliationMatch> Matched { get; set; } = new();

    /// <summary>Transactions PayPal reported that eShop has no record of.</summary>
    public List<ReconciliationPayPalEntry> InPayPalNotInEShop { get; set; } = new();

    /// <summary>
    /// eShop payment records (captures / refunds) in this range that did not appear in PayPal's report.
    /// In the sandbox this is expected for very recent activity because reporting lags by up to a few hours.
    /// </summary>
    public List<ReconciliationEShopEntry> InEShopNotInPayPal { get; set; } = new();
}

public record ReconciliationMatch(
    string PayPalTransactionId,
    string PayPalStatus,
    decimal PayPalAmount,
    string Currency,
    int OrderId,
    string RecordType);   // "Capture" or "Refund"

public record ReconciliationPayPalEntry(
    string PayPalTransactionId,
    string PayPalStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? InitiatedAt,
    string? EventCode);

public record ReconciliationEShopEntry(
    int OrderId,
    string RecordType,    // "Capture" or "Refund"
    string PayPalTransactionId,
    decimal Amount,
    string Currency);
