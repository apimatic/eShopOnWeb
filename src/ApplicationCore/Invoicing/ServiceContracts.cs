using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Customer details a correction may change on a bill.</summary>
public record CustomerDetails(string? Name, string? Email);

/// <summary>The full state of a bill as reported back to a shopper.</summary>
public record InvoiceDetails(
    string InvoiceId,
    int OrderId,
    string Status,
    string? ProviderStatus,
    string Currency,
    string Amount,
    DateOnly DueDate,
    string? CustomerName,
    string? CustomerEmail,
    bool Issued,
    string? PaymentLink,
    IReadOnlyList<InvoiceHistoryItem> History);

/// <summary>A shopper-facing summary of one of the caller's bills.</summary>
public record InvoiceSummary(
    string InvoiceId,
    int OrderId,
    string Status,
    string Amount,
    string Currency,
    DateOnly DueDate,
    bool Issued);

/// <summary>Whether a reconciliation entry is a bill eShop raised, or a foreign bill on the shared account.</summary>
public enum InvoiceOrigin
{
    EShop,
    External
}

/// <summary>How a reconciliation entry lines up between the provider's record and eShop's.</summary>
public enum ReconciliationDiscrepancy
{
    /// <summary>Present on both sides (or a foreign bill that is legitimately not eShop's).</summary>
    None,

    /// <summary>The provider knows of an eShop-originated bill that eShop's own records do not.</summary>
    MissingFromEShop,

    /// <summary>eShop believes it raised a bill that the provider's record for the range does not show.</summary>
    MissingFromProvider
}

public record ReconciliationEntry(
    string? InvoiceId,
    InvoiceOrigin Origin,
    bool PresentAtProvider,
    bool PresentInEShop,
    string? Status,
    string? Amount,
    string? Currency,
    DateTimeOffset? CreatedDate,
    ReconciliationDiscrepancy Discrepancy);

/// <summary>
/// A report lining the provider's record of bills raised in a date range up against what eShop
/// believes it raised, making plain which bills are eShop's and which are foreign to it.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    int MatchedCount,
    string Note,
    IReadOnlyList<ReconciliationEntry> Entries);
