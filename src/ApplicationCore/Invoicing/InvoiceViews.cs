using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single history event on a bill, as reported by the provider.</summary>
public record InvoiceHistoryEntry(string? Event, DateTimeOffset? Date);

/// <summary>
/// The full view of one bill: eShop's own facts plus the provider's freshly-read status, history and (once
/// issued) payment link. Returned by the single-bill read and by the issue/withdraw/correct actions.
/// </summary>
public record InvoiceDetails
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset DueDate { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }
    /// <summary>eShop's lifecycle stage: Draft / Issued / Withdrawn.</summary>
    public required string State { get; init; }
    /// <summary>The provider's own free-form status string.</summary>
    public string? ProviderStatus { get; init; }
    /// <summary>The customer-facing pay URL; null until the bill has been issued.</summary>
    public string? PaymentLink { get; init; }
    public required DateTimeOffset CreatedDate { get; init; }
    public IReadOnlyList<InvoiceHistoryEntry> History { get; init; } = Array.Empty<InvoiceHistoryEntry>();
}

/// <summary>A compact per-bill row for the caller's list of their own bills.</summary>
public record InvoiceSummaryView
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset DueDate { get; init; }
    public required string State { get; init; }
    public string? ProviderStatus { get; init; }
    public required DateTimeOffset CreatedDate { get; init; }
}

/// <summary>Which side(s) of the reconciliation a bill appears on, and whether it is eShop's at all.</summary>
public record ReconciliationEntry
{
    public required string InvoiceId { get; init; }
    /// <summary>"eShop" when the provider record carries eShop's stamp; "External" when raised by other activity.</summary>
    public required string Origin { get; init; }
    /// <summary>The provider has a record of this bill within the range.</summary>
    public required bool PresentAtProvider { get; init; }
    /// <summary>eShop has its own record of this bill within the range.</summary>
    public required bool PresentInEShop { get; init; }
    public string? Status { get; init; }
    public string? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
    /// <summary>The eShop order id, when eShop holds a record of this bill.</summary>
    public int? OrderId { get; init; }
}

/// <summary>Counts that summarise the line-up between the provider's record and eShop's.</summary>
public record ReconciliationSummary
{
    public required int Matched { get; init; }
    public required int EShopMissingAtProvider { get; init; }
    public required int ProviderMissingInEShop { get; init; }
    public required int ExternalAtProvider { get; init; }
}

/// <summary>
/// The reconciliation report over a date range: eShop's own record of the bills it raised in the range,
/// lined up against the provider's record, and the provider's record made plain as to which bills are
/// eShop's and which were raised by other activity on the shared account.
/// </summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int ProviderInvoicesScanned { get; init; }
    /// <summary>True if the provider list was longer than the scan cap, so its record may not be fully covered.</summary>
    public required bool Truncated { get; init; }
    /// <summary>
    /// Whether the provider's list supplies a created-date per bill. When false (as in this account), the
    /// date range is applied to eShop's own records; the provider's bills are cross-referenced by identifier
    /// and classified (eShop's vs external) but cannot themselves be date-filtered.
    /// </summary>
    public required bool ProviderCreatedDatesAvailable { get; init; }
    public required ReconciliationSummary Summary { get; init; }
    public required IReadOnlyList<ReconciliationEntry> Entries { get; init; }
}
