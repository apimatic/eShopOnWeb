using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Provider-agnostic contracts for the invoicing provider (Visa via CyberSource). The
/// application core depends only on these; the concrete SDK lives in Infrastructure.
/// </summary>

/// <summary>A single billed line, sourced from the order's items.</summary>
public record ProviderLineItem(
    string ProductName,
    string? Sku,
    int Quantity,
    decimal UnitPrice,
    decimal TotalAmount);

/// <summary>What is needed to raise a new bill with the provider.</summary>
public record NewInvoiceRequest
{
    public required string OrderReference { get; init; }
    public required string Description { get; init; }
    public required DateTime DueDate { get; init; }
    public required string Currency { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }
    public required string CustomerId { get; init; }
    public IReadOnlyList<ProviderLineItem> LineItems { get; init; } = Array.Empty<ProviderLineItem>();
}

/// <summary>
/// A correction to a bill that has not yet been put to the shopper. The amount and line items
/// still come from the order (they are re-sent unchanged); only the due date and customer
/// details are actually being corrected.
/// </summary>
public record InvoiceCorrection
{
    public required string OrderReference { get; init; }
    public required string Description { get; init; }
    public required DateTime DueDate { get; init; }
    public required string Currency { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }
    public required string CustomerId { get; init; }
    public IReadOnlyList<ProviderLineItem> LineItems { get; init; } = Array.Empty<ProviderLineItem>();
}

/// <summary>An event in the provider's own history of how a bill reached its current state.</summary>
public record ProviderInvoiceHistoryEvent(string? Event, DateTimeOffset? Date);

/// <summary>A snapshot of a single bill as the provider currently reports it.</summary>
public record ProviderInvoiceSnapshot
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? PaymentLink { get; init; }
    public DateTime? DueDate { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public DateTimeOffset? SubmitTimeUtc { get; init; }
    public IReadOnlyList<ProviderInvoiceHistoryEvent> History { get; init; } = Array.Empty<ProviderInvoiceHistoryEvent>();
}

/// <summary>A bill as it appears in the provider's list/reconciliation feed.</summary>
public record ProviderInvoiceListItem
{
    public required string Id { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerId { get; init; }
}
