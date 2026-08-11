using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A reconciliation of PayPal's own transaction record against eShop's orders for a date range, so a
/// payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotInPayPal);

/// <summary>A PayPal transaction that lines up with an eShop order.</summary>
public record ReconciliationMatch(
    int OrderId,
    string TransactionId,
    decimal? PayPalAmount,
    decimal EShopAmount,
    string? PayPalStatus,
    string EShopStatus,
    bool AmountsAgree);

/// <summary>A transaction PayPal reports that has no matching eShop order.</summary>
public record ReconciliationPayPalOnly(
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? Status);

/// <summary>An eShop payment that PayPal's report does not (yet) show.</summary>
public record ReconciliationEShopOnly(
    int OrderId,
    string? PayPalOrderId,
    string? CaptureId,
    decimal Amount,
    string Status);
