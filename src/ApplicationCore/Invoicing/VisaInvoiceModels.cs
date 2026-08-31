using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Customer details a bill is addressed to. Invented test-fixture data only in the sandbox.</summary>
public record VisaCustomer(string Name, string Email);

/// <summary>A single billed line, mirrored from an order item.</summary>
public record VisaInvoiceLine(string ProductName, string? Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// What eShop asks the provider to bill. Every monetary value here is derived from the order, never
/// from anything a caller restates.
/// </summary>
public record VisaInvoiceDraft(
    decimal Amount,
    string Currency,
    string Description,
    DateOnly DueDate,
    VisaCustomer Customer,
    IReadOnlyList<VisaInvoiceLine> Lines);

/// <summary>A single event in the provider's account of how a bill reached its current state.</summary>
public record VisaInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>
/// A bill's state as the provider reports it: the provider identifier, where it currently stands, and
/// — once it has been put to the shopper — how they can pay it.
/// </summary>
public record VisaInvoiceState(
    string ProviderInvoiceId,
    string Status,
    string? PaymentLink,
    decimal? Amount,
    string? Currency,
    DateOnly? DueDate,
    string? CustomerName,
    string? CustomerEmail,
    string? Description,
    DateTimeOffset? SubmittedUtc,
    IReadOnlyList<VisaInvoiceEvent> History);

/// <summary>
/// One row of the provider's own record of a bill, as returned by the provider's list-invoices
/// capability. Used to reconcile the provider's ledger against what eShop believes it raised.
/// </summary>
public record VisaProviderInvoice(
    string ProviderInvoiceId,
    string Status,
    DateTimeOffset? CreatedUtc,
    decimal? Amount,
    string? Currency,
    string? CustomerName);
