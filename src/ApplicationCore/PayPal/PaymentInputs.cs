using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>One line of a placed order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order: either one-off <see cref="Card"/> details, or a reference to one of the shopper's
/// <see cref="SavedCardId"/>. Exactly one must be provided.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedCardId);

/// <summary>Which side of the reconciliation an entry sits on.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal and eShop both know this transaction.</summary>
    Matched,

    /// <summary>PayPal reports it but eShop has no record.</summary>
    PayPalOnly,

    /// <summary>eShop recorded it but PayPal's report does not include it (for the range).</summary>
    EShopOnly
}

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? EShopReference,
    int? OrderId,
    string ReferenceKind,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Status);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transactions lined up against eShop orders, so a
/// payment one side knows about and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
