using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The full picture of a bill returned to a shopper: eShop's own record joined with whatever the
/// provider currently reports about the bill's state, its history, and how it can be paid.
/// </summary>
public record InvoiceDetailView(
    int InvoiceId,
    int OrderId,
    string ProviderInvoiceId,
    string InvoiceNumber,
    InvoiceStatus Status,
    string ProviderStatus,
    decimal Amount,
    string CurrencyCode,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string? PaymentLink,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? WithdrawnAt,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>Where a reconciled bill is known.</summary>
public enum ReconciliationSource
{
    /// <summary>The provider knows about it and eShop believes it raised it.</summary>
    Matched = 0,

    /// <summary>The provider knows about it but eShop has no record of it (another activity's bill).</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it raised it but the provider's record does not show it in range.</summary>
    EShopOnly = 2
}

/// <summary>A single reconciled bill, lining up the provider's record against eShop's.</summary>
public record ReconciliationEntry(
    string ProviderInvoiceId,
    string InvoiceNumber,
    ReconciliationSource Source,
    bool IsEShopInvoice,
    string? ProviderStatus,
    InvoiceStatus? EShopStatus,
    int? EShopInvoiceId,
    int? OrderId,
    decimal? Amount,
    string? CurrencyCode,
    string? CustomerName,
    DateTimeOffset? RaisedAt);

/// <summary>
/// The reconciliation report over a date range: every provider bill raised in the window and every
/// eShop bill raised in the window, lined up so that a bill known to only one side is plainly visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
