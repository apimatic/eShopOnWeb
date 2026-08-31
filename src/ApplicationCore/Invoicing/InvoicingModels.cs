using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The correctable fields of a bill. Any field left null is kept as-is.</summary>
public sealed record InvoiceCorrection(DateOnly? DueDate, string? CustomerName, string? CustomerEmail);

/// <summary>Outcome of placing an order.</summary>
public sealed record PlacedOrderResult(int OrderId, decimal Total, string Currency, int ItemCount);

/// <summary>Outcome of raising a bill against an order.</summary>
public sealed record RaisedInvoiceResult(
    string InvoiceId,
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    DateOnly DueDate);

/// <summary>One event in a bill's provider-owned history.</summary>
public sealed record InvoiceEvent(string Event, DateTimeOffset? Date);

/// <summary>
/// The full view of a bill: eShop's lifecycle status, the state the provider reports (and how it got
/// there), the billed facts, and — only once the bill has been put to the shopper — the way to pay it.
/// </summary>
public sealed record InvoiceDetailsResult(
    string InvoiceId,
    int OrderId,
    string Status,
    string? ProviderStatus,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    DateTimeOffset RaisedAt,
    string? PaymentLink,
    IReadOnlyList<InvoiceEvent> History);

/// <summary>A compact view of a bill for the shopper's list.</summary>
public sealed record InvoiceSummaryResult(
    string InvoiceId,
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    DateTimeOffset RaisedAt);

/// <summary>Which side(s) know about a bill within a reconciliation range.</summary>
public enum ReconciliationSource
{
    /// <summary>Both the provider and eShop have a record of this bill.</summary>
    RecordedByBoth = 0,

    /// <summary>The provider knows about this bill but eShop does not (it is not this application's).</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it raised this bill but the provider has no record of it in range.</summary>
    EShopOnly = 2
}

/// <summary>One line of the reconciliation report.</summary>
public sealed record ReconciliationEntry(
    string InvoiceId,
    ReconciliationSource Source,
    string? ProviderStatus,
    string? EShopStatus,
    int? OrderId,
    decimal? Amount,
    string? Currency,
    string? CustomerName,
    DateTimeOffset? CreatedDate);

/// <summary>
/// The provider's record of bills raised in a range lined up against what eShop believes it raised,
/// making plain which bills are eShop's, which are the provider's alone, and which eShop has no
/// provider record for.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    int RecordedByBothCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
