using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>An eShop-side money movement (a capture or a refund) that has a PayPal id.</summary>
public record EShopTransaction(string TransactionId, string Kind, decimal Amount, int OrderId, string? Status, DateTimeOffset Date);

/// <summary>A transaction that both PayPal and eShop have a record of, lined up.</summary>
public record ReconciliationMatch(
    string TransactionId,
    string Kind,
    int OrderId,
    decimal EShopAmount,
    decimal PayPalAmount,
    string? PayPalStatus,
    bool AmountMatches);

/// <summary>
/// Reconciliation of PayPal's own transaction record against eShop's, over a date range.
/// Discrepancies in either direction are surfaced explicitly.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<EShopTransaction> InEShopNotInPayPal);
