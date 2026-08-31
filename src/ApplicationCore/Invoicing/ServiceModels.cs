using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A requested order line: which catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Outcome of placing an order. Either an id or a validation error, never both.</summary>
public record OrderPlacementResult(int? OrderId, string? Error)
{
    public bool Succeeded => OrderId is not null;
    public static OrderPlacementResult Ok(int orderId) => new(orderId, null);
    public static OrderPlacementResult Fail(string error) => new(null, error);
}

/// <summary>One event in a bill's provider-reported history.</summary>
public record InvoiceHistoryEntry(string? Event, DateTimeOffset? Date);

/// <summary>The full view of a single bill returned to a caller.</summary>
public record InvoiceView(
    string InvoiceId,
    int OrderId,
    string State,
    string? ProviderStatus,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    string? CustomerName,
    string? CustomerEmail,
    string? Description,
    string? PaymentLink,
    IReadOnlyList<InvoiceHistoryEntry> History);

/// <summary>A compact view of a bill for the shopper's list.</summary>
public record MyInvoiceView(
    string InvoiceId,
    int OrderId,
    string State,
    string? ProviderStatus,
    decimal Amount,
    string Currency,
    DateOnly DueDate);

/// <summary>How a reconciliation entry lines up between the provider and eShop.</summary>
public enum ReconciliationMatch
{
    /// <summary>Known to both the provider and eShop.</summary>
    Matched,

    /// <summary>The provider has it but eShop does not — e.g. a bill raised by other activity.</summary>
    ProviderOnly,

    /// <summary>eShop believes it raised it but the provider did not return it.</summary>
    EShopOnly
}

/// <summary>A single line of the reconciliation report.</summary>
public record ReconciliationEntry(
    string InvoiceId,
    ReconciliationMatch Match,
    bool BelongsToEShop,
    string? ProviderStatus,
    DateTimeOffset? CreatedDate,
    decimal? Amount,
    string? Currency,
    string? CustomerName,
    int? OrderId,
    string? BuyerId,
    string? EShopState);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
