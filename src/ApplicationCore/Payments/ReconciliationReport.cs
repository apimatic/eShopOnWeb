using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation of eShop's captured payments against PayPal's own transaction record
/// for a date range. Any payment PayPal knows about that eShop doesn't — or the reverse —
/// shows up as an unmatched entry.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledEntry> Matched,
    IReadOnlyList<EShopOnlyEntry> InEShopNotInPayPal,
    IReadOnlyList<PayPalOnlyEntry> InPayPalNotInEShop)
{
    public int PayPalTransactionCount { get; init; }
    public int EShopPaymentCount { get; init; }
}

/// <summary>An eShop payment matched to one or more PayPal transactions by invoice id.</summary>
public record ReconciledEntry(
    int OrderId,
    string InvoiceId,
    decimal EShopAmount,
    decimal PayPalAmount,
    IReadOnlyList<string> PayPalTransactionIds);

/// <summary>An eShop payment that PayPal's record does not (yet) show.</summary>
public record EShopOnlyEntry(int OrderId, string InvoiceId, decimal Amount, string Status);

/// <summary>A PayPal transaction with no matching eShop order.</summary>
public record PayPalOnlyEntry(
    string TransactionId,
    string? InvoiceId,
    decimal Amount,
    string Status,
    string EventCode,
    DateTimeOffset InitiationDate);
