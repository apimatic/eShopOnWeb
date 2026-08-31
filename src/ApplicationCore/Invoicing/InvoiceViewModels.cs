using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Application-level view models returned by <see cref="Interfaces.IInvoiceService"/>. They combine
/// eShop's own record of a bill with the authoritative state fetched from the provider, and are what
/// the API layer shapes into HTTP responses.
/// </summary>

public record InvoiceDetails(
    string InvoiceId,
    int OrderId,
    string BuyerId,
    string Status,
    bool PutToShopper,
    bool Withdrawn,
    decimal Amount,
    string Currency,
    string Description,
    string CustomerName,
    string CustomerEmail,
    DateTime DueDate,
    string? PaymentLink,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProviderInvoiceEvent> ProviderHistory);

/// <summary>The outcome of raising a bill: the provider identifier and its initial status.</summary>
public record RaisedInvoice(string InvoiceId, string Status);

public record InvoiceSummaryView(
    string InvoiceId,
    int OrderId,
    string Status,
    bool PutToShopper,
    bool Withdrawn,
    decimal Amount,
    string Currency,
    DateTime DueDate,
    string CustomerName,
    string? PaymentLink);

/// <summary>How a single bill lines up between the provider's record and eShop's.</summary>
public enum ReconciliationMatch
{
    /// <summary>Known to both the provider and eShop.</summary>
    Matched,

    /// <summary>The provider knows about it but eShop does not — not this application's bill.</summary>
    ProviderOnly,

    /// <summary>eShop believes it raised it but the provider's list does not include it.</summary>
    EShopOnly
}

public record ReconciliationEntry(
    string InvoiceId,
    ReconciliationMatch Match,
    bool BelongsToEShop,
    string? ProviderStatus,
    string? EShopStatus,
    int? OrderId,
    string? BuyerId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? CreatedDate,
    string? CustomerName);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
