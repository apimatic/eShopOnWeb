using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A single billed line handed to the provider when a bill is raised. <see cref="ProductSku"/> identifies
/// the catalog item; the provider requires a non-empty sku on every line.
/// </summary>
public record InvoiceLine(string ProductSku, string ProductName, int Quantity, decimal UnitPrice, decimal TotalAmount);

/// <summary>
/// Everything the provider needs to raise a bill. Amounts are eShop domain <see cref="decimal"/>s; the
/// provider adapter is responsible for formatting them onto the wire.
/// </summary>
public record RaiseInvoiceCommand(
    string MerchantReference,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset DueDate,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>
/// A correction to a draft bill. The provider's update is not a partial patch, so the amount and
/// description are re-sent unchanged (what is billed still comes from the order) alongside the new
/// due date and customer details.
/// </summary>
public record CorrectInvoiceCommand(
    string MerchantReference,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset DueDate,
    string CustomerName,
    string CustomerEmail);

/// <summary>A provider-reported state-change event in a bill's history.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>
/// The provider's view of a single bill, as read back from it. <see cref="Status"/> is an opaque,
/// free-form string (this provider exposes no closed status enum). <see cref="PaymentLink"/> is null
/// until the bill has been issued.
/// </summary>
public record ProviderInvoice(
    string Id,
    string? Status,
    string? PaymentLink,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>
/// A row from the provider's own list of bills, used for reconciliation. <see cref="MerchantReference"/>
/// is the merchantCustomerId eShop stamped at create time — null/foreign values are bills eShop did not
/// raise. <see cref="CreatedDateRaw"/> is the provider's created-date string, parsed best-effort.
/// </summary>
public record ProviderInvoiceSummary(
    string? Id,
    string? Status,
    string? CreatedDateRaw,
    string? MerchantReference,
    string? CustomerName,
    string? TotalAmount,
    string? Currency);

/// <summary>One page of the provider's bill list, plus the provider's own total count for paging.</summary>
public record ProviderInvoicePage(IReadOnlyList<ProviderInvoiceSummary> Items, int TotalInvoices);
