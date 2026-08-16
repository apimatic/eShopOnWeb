using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>One reconciled row: an eShop order, a PayPal transaction, or a matched pair.</summary>
public record ReconciliationLine(
    string MatchState,             // "Matched", "OnlyInPayPal", "OnlyInEshop"
    int? EshopOrderId,
    string? PayPalTransactionId,
    string? PayPalOrderId,
    string? CaptureId,
    string? InvoiceId,
    decimal? EshopAmount,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? Date);

/// <summary>
/// PayPal's record of transactions for a date range lined up against eShop orders, so a payment
/// PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> OnlyInPayPal,
    IReadOnlyList<ReconciliationLine> OnlyInEshop)
{
    public int PayPalTransactionCount => Matched.Count + OnlyInPayPal.Count;
    public int EshopOrderCount => Matched.Count + OnlyInEshop.Count;
}
