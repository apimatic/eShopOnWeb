using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single billed line to send to the provider.</summary>
public record ProviderInvoiceLine(string ProductSku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>
/// The full billed detail sent to the provider when raising or correcting a bill.
/// The amount always originates from the order, never from a caller's restatement.
/// </summary>
public record ProviderInvoiceDraft(
    string? InvoiceNumber,
    string Description,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string CurrencyCode,
    decimal TotalAmount,
    IReadOnlyList<ProviderInvoiceLine> Lines);

/// <summary>One event in a bill's provider-side history (how it reached its state).</summary>
public record ProviderInvoiceEvent(string Event, DateTimeOffset? Date);

/// <summary>
/// The provider's current record of a bill: its identifier there, where it stands,
/// how it can be paid once issued, and the history behind its state.
/// </summary>
public record ProviderInvoiceState(
    string Id,
    string? InvoiceNumber,
    string Status,
    string? PaymentLink,
    DateOnly? DueDate,
    decimal? TotalAmount,
    string? CurrencyCode,
    string? CustomerName,
    string? CustomerEmail,
    string? Description,
    DateTimeOffset? CreatedDate,
    IReadOnlyList<ProviderInvoiceEvent> History);
