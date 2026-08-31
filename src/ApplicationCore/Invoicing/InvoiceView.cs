using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The full state of a single bill as eShop presents it to a caller: eShop's linkage
/// (invoice/order), the provider's current status and history, and — once the bill has been
/// put to the shopper — how it can be paid.
/// </summary>
public class InvoiceView
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required string Status { get; init; }
    public required string CurrencyCode { get; init; }
    public decimal Amount { get; init; }
    public DateOnly DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }

    /// <summary>How the shopper can pay the bill. Present only once the bill has been issued and while it remains payable.</summary>
    public string? PaymentLink { get; init; }

    /// <summary>Whatever the provider reports about how this bill reached its current state.</summary>
    public IReadOnlyList<InvoiceHistoryEntry> History { get; init; } = Array.Empty<InvoiceHistoryEntry>();
}

public class InvoiceHistoryEntry
{
    public string? Event { get; init; }
    public DateTimeOffset? Date { get; init; }
}

/// <summary>A shopper-facing summary of one of the caller's own bills.</summary>
public class InvoiceSummaryView
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required string CurrencyCode { get; init; }
    public decimal Amount { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
