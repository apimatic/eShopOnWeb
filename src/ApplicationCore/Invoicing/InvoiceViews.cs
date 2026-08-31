using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Optional customer details a caller may supply when raising or correcting a bill.</summary>
public record CustomerDetails(string? Name, string? Email);

/// <summary>One event in how a bill reached its current state, as the provider reports it.</summary>
public record InvoiceHistoryView(string Event, DateTimeOffset? Date);

/// <summary>Full view of a single bill returned by the shopper-facing GET endpoint.</summary>
public class InvoiceDetailView
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    /// <summary>eShop's authoritative lifecycle state: Raised, Issued or Withdrawn.</summary>
    public required string State { get; init; }
    /// <summary>The status the provider currently reports for the bill.</summary>
    public string? ProviderStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    /// <summary>How the shopper can pay the bill — present only once the bill has been put to them.</summary>
    public string? PaymentLink { get; init; }
    /// <summary>Whatever the provider reports about how the bill reached its state.</summary>
    public IReadOnlyList<InvoiceHistoryView> History { get; init; } = Array.Empty<InvoiceHistoryView>();
}

/// <summary>An entry in the caller's list of their own bills.</summary>
public class InvoiceListItemView
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string State { get; init; }
    public string? ProviderStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
}

/// <summary>How a provider bill lines up against eShop's own records in a reconciliation report.</summary>
public enum ReconciliationClassification
{
    /// <summary>Both the provider and eShop have a record of this bill.</summary>
    Matched = 0,
    /// <summary>The provider knows about this bill but eShop does not — it may not be this application's.</summary>
    ProviderOnly = 1,
    /// <summary>eShop believes it raised this bill but the provider did not return it in the range.</summary>
    EShopOnly = 2
}

/// <summary>One line of the reconciliation report.</summary>
public class ReconciliationEntryView
{
    public required string InvoiceId { get; init; }
    public required string Classification { get; init; }
    /// <summary>True when the provider bill carries eShop's invoice-number marker.</summary>
    public bool BearsEShopMarker { get; init; }
    public string? ProviderStatus { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public DateTimeOffset? RaisedAt { get; init; }
    /// <summary>eShop's order/buyer for the bill, when eShop has a record of it.</summary>
    public int? OrderId { get; init; }
    public string? BuyerId { get; init; }
}

/// <summary>Headline counts for a reconciliation report.</summary>
public class ReconciliationSummaryView
{
    public int TotalProviderInvoicesInRange { get; init; }
    public int Matched { get; init; }
    public int ProviderOnly { get; init; }
    public int EShopOnly { get; init; }
}

/// <summary>The reconciliation report over a date range.</summary>
public class ReconciliationReportView
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required ReconciliationSummaryView Summary { get; init; }
    public IReadOnlyList<ReconciliationEntryView> Entries { get; init; } = Array.Empty<ReconciliationEntryView>();
}
