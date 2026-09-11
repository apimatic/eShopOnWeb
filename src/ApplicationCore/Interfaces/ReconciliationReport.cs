using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The result of lining PayPal's transaction record up against eShop payments for a date range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotInPayPal);

/// <summary>A PayPal transaction that matches an eShop order (by invoice reference).</summary>
public record ReconciliationMatch(
    string TransactionId,
    string InvoiceId,
    int OrderId,
    decimal PayPalAmount,
    decimal EShopAmount,
    string PayPalStatus,
    string EShopStatus,
    bool AmountsAgree);

/// <summary>A transaction PayPal knows about with no matching eShop order.</summary>
public record ReconciliationPayPalOnly(
    string TransactionId,
    string? InvoiceId,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string EventCode,
    DateTimeOffset InitiationDate);

/// <summary>An eShop payment PayPal's report does not (yet) show.</summary>
public record ReconciliationEShopOnly(
    int OrderId,
    string InvoiceId,
    decimal Amount,
    string CurrencyCode,
    string Status);
