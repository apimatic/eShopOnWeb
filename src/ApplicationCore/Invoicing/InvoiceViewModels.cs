using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>One billed line as reported back to a caller.</summary>
public record InvoiceLineView(string ProductSku, string ProductName, int Units, decimal UnitPrice, decimal LineTotal);

/// <summary>One step in how a bill reached its current state, as the provider reports it.</summary>
public record InvoiceEventView(string Event, DateTimeOffset? Date);

/// <summary>
/// A bill's current state as reported to a caller: eShop's local lifecycle, the
/// richer status the provider owns, the history behind that status, and — once the
/// bill has been put to the shopper — how it can be paid.
/// </summary>
public record InvoiceDetailView(
    string InvoiceId,
    int OrderId,
    string BuyerId,
    string LocalStatus,
    string ProviderStatus,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string Description,
    string? PaymentLink,
    IReadOnlyList<InvoiceLineView> Lines,
    IReadOnlyList<InvoiceEventView> History);

/// <summary>A shopper's bill in a list, showing where it has got to.</summary>
public record InvoiceSummaryView(
    string InvoiceId,
    int OrderId,
    string LocalStatus,
    string ProviderStatus,
    decimal Amount,
    string Currency,
    DateOnly DueDate);

/// <summary>Which side(s) of the reconciliation a bill appears on.</summary>
public enum ReconciliationSource
{
    /// <summary>The provider has a record and eShop also believes it raised it.</summary>
    Both = 0,

    /// <summary>The provider has a record eShop does not — either another activity's bill or drift.</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it raised a bill the provider has no record of in range.</summary>
    EShopOnly = 2
}

/// <summary>
/// One line of the reconciliation report: a bill lined up between the provider's
/// record and what eShop believes it raised.
/// </summary>
public record ReconciliationEntry(
    string InvoiceId,
    ReconciliationSource Source,
    bool BelongsToEShop,
    string? ProviderStatus,
    string? LocalStatus,
    decimal? Amount,
    string? Currency,
    DateOnly? DueDate,
    DateTimeOffset? RaisedAt,
    string? CustomerName,
    int? OrderId);

/// <summary>
/// The operator reconciliation report over a date range: the provider's own record
/// of bills raised in the range, lined up against what eShop believes it raised,
/// with each bill marked as belonging to eShop or not.
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
