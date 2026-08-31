using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single billed line, mirrored from an order item.</summary>
public record GatewayInvoiceLine(string ProductSku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>Everything the provider needs to raise a new bill. Amount/lines are derived from the order.</summary>
public record GatewayInvoiceDraft(
    string InvoiceNumber,
    string Description,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<GatewayInvoiceLine> Lines);

/// <summary>
/// A correction to a held bill. The amount and lines are still derived from the order (not
/// correctable by the caller); only the due date and customer details change.
/// </summary>
public record GatewayInvoiceCorrection(
    string Description,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<GatewayInvoiceLine> Lines);

/// <summary>One entry from the provider's record of how a bill reached its current state.</summary>
public record GatewayHistoryEntry(string Event, DateTimeOffset? Date);

/// <summary>The provider's view of a bill as read back from it.</summary>
public record GatewayInvoice(
    string Id,
    string? InvoiceNumber,
    string? Status,
    string? PaymentLink,
    decimal? TotalAmount,
    string? Currency,
    string? CustomerName,
    DateOnly? DueDate,
    DateTimeOffset? RaisedAt,
    IReadOnlyList<GatewayHistoryEntry> History);

/// <summary>A lighter provider record used when reconciling a whole date range.</summary>
public record GatewayInvoiceSummary(
    string Id,
    string? InvoiceNumber,
    string? Status,
    decimal? TotalAmount,
    string? Currency,
    string? CustomerName,
    DateTimeOffset? RaisedAt);
