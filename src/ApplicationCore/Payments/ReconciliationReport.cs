using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation of PayPal's own transaction record for a date range against eShop's orders,
/// so a payment PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<GatewayTransaction> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotInPayPal);

/// <summary>A PayPal transaction lined up with the eShop order it settles.</summary>
public record ReconciliationMatch(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    int OrderId,
    string EShopPaymentStatus);

/// <summary>An eShop payment captured in the range that has no PayPal transaction lined up with it.</summary>
public record ReconciliationEShopOnly(
    int OrderId,
    string EShopPaymentStatus,
    string? CaptureId,
    decimal? CapturedAmount);
