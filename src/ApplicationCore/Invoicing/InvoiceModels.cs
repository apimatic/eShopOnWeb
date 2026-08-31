using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>One entry of the provider's account of how a bill reached its current state.</summary>
public sealed record InvoiceHistoryEntry(string? Event, DateTimeOffset? Date);

/// <summary>
/// The full state of a bill: eShop's local lifecycle position, whatever the provider currently
/// reports, how it got there, and — once put to the shopper and still payable — how to pay it.
/// </summary>
public sealed record InvoiceDetails
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }

    /// <summary>eShop's local lifecycle status (Draft/Issued/Withdrawn).</summary>
    public required string LocalStatus { get; init; }

    /// <summary>The provider's own status string (authoritative), as last read from the provider.</summary>
    public string? ProviderStatus { get; init; }

    public DateTimeOffset DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public required string Currency { get; init; }
    public decimal Amount { get; init; }

    /// <summary>The customer-facing pay URL. Only handed out once the bill is issued and still payable.</summary>
    public string? PaymentLink { get; init; }

    public IReadOnlyList<InvoiceHistoryEntry> History { get; init; } = Array.Empty<InvoiceHistoryEntry>();
}

/// <summary>A compact view of one of a shopper's bills for the list endpoint.</summary>
public sealed record InvoiceSummary
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string LocalStatus { get; init; }
    public DateTimeOffset DueDate { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Where a reconciled bill sits relative to the two sides of the ledger.</summary>
public enum ReconciliationPresence
{
    /// <summary>Present both at the provider and in eShop's own records.</summary>
    Matched,

    /// <summary>The provider knows about it but eShop does not (another activity's bill on the shared account).</summary>
    ProviderOnly,

    /// <summary>eShop believes it raised it but the provider did not return it in the range.</summary>
    EShopOnly
}

/// <summary>One line of the reconciliation report.</summary>
public sealed record ReconciliationEntry
{
    public required string InvoiceId { get; init; }
    public required ReconciliationPresence Presence { get; init; }

    /// <summary>True when this bill belongs to eShop (Matched or EShopOnly); false for another activity's bill.</summary>
    public bool IsEShopInvoice => Presence != ReconciliationPresence.ProviderOnly;

    // Provider-side facts (present for Matched / ProviderOnly)
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? ProviderCreatedDate { get; init; }

    // eShop-side facts (present for Matched / EShopOnly)
    public int? OrderId { get; init; }
    public string? LocalStatus { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

/// <summary>The operator's reconciliation report over a date range.</summary>
public sealed record ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int ProviderInvoiceCount { get; init; }
    public int EShopInvoiceCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();
}
