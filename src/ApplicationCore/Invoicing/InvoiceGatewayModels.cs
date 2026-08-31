using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Provider-agnostic models exchanged with an <see cref="Interfaces.IInvoiceGateway"/>. They keep the
/// concrete payment-provider SDK types out of the application and API layers, so the invoicing
/// provider could be swapped without touching business logic or endpoints.
/// </summary>

/// <summary>A single billed line, sourced from the order.</summary>
public record InvoiceLine(string ProductName, int Quantity, decimal UnitPrice, string? Sku);

/// <summary>Everything needed to raise a brand-new bill with the provider.</summary>
public record InvoiceDraft(
    int OrderId,
    string CustomerName,
    string CustomerEmail,
    DateTime DueDate,
    string Description,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>
/// A correction to a bill that has not yet been put to the shopper. The amount and lines are still
/// sourced from the order — only the due date and customer details are the caller's to change.
/// </summary>
public record InvoiceAmendment(
    DateTime DueDate,
    string Description,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>A single step in the provider's account of how a bill reached its current state.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>The provider's full view of one bill.</summary>
public record ProviderInvoice(
    string Id,
    string Status,
    string? PaymentLink,
    DateTimeOffset? CreatedDate,
    string? CustomerName,
    string? CustomerEmail,
    decimal? Amount,
    string? Currency,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>A lightweight provider record used when listing bills for reconciliation.</summary>
public record ProviderInvoiceSummary(
    string Id,
    string Status,
    DateTimeOffset? CreatedDate,
    string? CustomerName,
    decimal? Amount,
    string? Currency);
