using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The result of lining PayPal's own transaction record up against eShop orders for a date range. A
/// payment PayPal knows about but eShop does not (or the reverse) shows up in the unmatched lists.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationTransaction> UnmatchedInPayPal,
    IReadOnlyList<ReconciliationEShopOrder> UnmatchedInEShop);

/// <summary>A PayPal transaction that lines up with an eShop order.</summary>
public record ReconciliationMatch(
    int OrderId,
    string? CaptureId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    decimal? EShopCapturedAmount,
    string EShopPaymentStatus);

/// <summary>An eShop order with a captured payment that PayPal's report does not (yet) show.</summary>
public record ReconciliationEShopOrder(
    int OrderId,
    string? CaptureId,
    decimal? CapturedAmount,
    string PaymentStatus);
