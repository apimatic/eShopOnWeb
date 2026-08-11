using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// A reconciliation report over a date range: PayPal's own record of transactions lined up against
/// eShop orders, so a payment PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int EShopCapturedOrderCount { get; init; }

    /// <summary>PayPal transactions matched to an eShop order (by invoice id or capture/transaction id).</summary>
    public List<ReconciliationMatch> Matched { get; init; } = new();

    /// <summary>Transactions PayPal reports that no eShop order accounts for.</summary>
    public List<ReconciliationPayPalOnly> InPayPalOnly { get; init; } = new();

    /// <summary>eShop orders captured in the range that PayPal's report does not (yet) show.</summary>
    public List<ReconciliationEShopOnly> InEShopOnly { get; init; } = new();
}

public sealed record ReconciliationMatch(
    int OrderId,
    string? PayPalCaptureId,
    string? PayPalTransactionId,
    decimal? EShopCapturedAmount,
    decimal? PayPalAmount,
    string? PayPalStatus,
    bool AmountsAgree);

public sealed record ReconciliationPayPalOnly(
    string? TransactionId,
    string? InvoiceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date);

public sealed record ReconciliationEShopOnly(
    int OrderId,
    string? PayPalCaptureId,
    decimal? CapturedAmount,
    string? Currency,
    DateTimeOffset OrderDate);
