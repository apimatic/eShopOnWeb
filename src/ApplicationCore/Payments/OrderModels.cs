using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How a PayPal transaction lines up against eShop's own records.</summary>
public enum ReconciliationMatch
{
    /// <summary>Present in both PayPal and eShop.</summary>
    Matched = 0,

    /// <summary>PayPal knows about it but eShop has no matching record.</summary>
    PayPalOnly = 1,

    /// <summary>eShop recorded it but PayPal's report for this range does not list it.</summary>
    EShopOnly = 2
}

/// <summary>A single reconciled row lining up a PayPal transaction against an eShop order/payment.</summary>
public sealed record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    int? OrderId,
    string? EShopReference,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

/// <summary>The reconciliation report over a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
