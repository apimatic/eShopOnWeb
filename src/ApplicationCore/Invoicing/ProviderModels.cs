using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single line to bill for. Sourced from the order, never from caller input.</summary>
public record NewInvoiceLine(
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal TotalAmount);

/// <summary>Customer/payer details carried on a bill. Correctable while the bill is a draft.</summary>
public record CustomerDetails(string Name, string Email);

/// <summary>Everything the provider needs to raise a new bill. Amounts come from the order.</summary>
public record NewInvoice(
    int OrderId,
    string Description,
    string ReferenceNumber,
    DateOnly DueDate,
    string Currency,
    decimal TotalAmount,
    CustomerDetails Customer,
    IReadOnlyList<NewInvoiceLine> Lines);

/// <summary>
/// A correction applied to a draft bill: the due date and customer details may change, but the
/// billed amount still comes from the order and is restated here so the provider keeps a consistent
/// record.
/// </summary>
public record InvoiceAmendment(
    string Description,
    DateOnly DueDate,
    string Currency,
    decimal TotalAmount,
    CustomerDetails Customer,
    IReadOnlyList<NewInvoiceLine> Lines);

/// <summary>One entry from the provider's account of how a bill reached its current state.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>The provider's full view of a single bill.</summary>
public record ProviderInvoice(
    string Id,
    string? Status,
    string? PaymentLink,
    DateOnly? DueDate,
    decimal? Amount,
    string? Currency,
    string? CustomerName,
    string? CustomerEmail,
    string? Description,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>A lightweight view of a bill as returned by the provider's list endpoint.</summary>
public record ProviderInvoiceSummary(
    string Id,
    string? Status,
    DateTimeOffset? CreatedDate,
    string? CustomerName,
    decimal? Amount,
    string? Currency);
