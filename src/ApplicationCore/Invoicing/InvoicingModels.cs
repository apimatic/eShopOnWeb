using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>One billable line on a bill. Money is a domain <see cref="decimal"/>; the provider adapter
/// is responsible for formatting it for the wire. <see cref="Sku"/> identifies the product (the provider
/// requires a SKU per line).</summary>
public record InvoiceLineItem(string ProductName, decimal UnitPrice, int Quantity, string Sku);

/// <summary>The customer a bill is addressed to. <see cref="MerchantCustomerId"/> is an application-controlled
/// tag the provider echoes back, used to line up the provider's record with eShop's own.</summary>
public record InvoiceCustomer(string Name, string? Email, string MerchantCustomerId);

/// <summary>Everything the provider needs to raise a bill. The amount and lines derive from the order.</summary>
public record RaiseInvoiceCommand(
    string Description,
    DateTimeOffset DueDate,
    decimal TotalAmount,
    string Currency,
    InvoiceCustomer Customer,
    IReadOnlyList<InvoiceLineItem> Lines);

/// <summary>Everything the provider needs to correct a draft bill. The update is a full replacement at the
/// provider, so the amount and lines are re-hydrated from the order every time.</summary>
public record UpdateInvoiceCommand(
    string Description,
    DateTimeOffset DueDate,
    decimal TotalAmount,
    string Currency,
    InvoiceCustomer Customer,
    IReadOnlyList<InvoiceLineItem> Lines);

/// <summary>One step in the provider's own record of how a bill reached its current state.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>The provider's view of a single bill, as returned by create/get/update/issue/withdraw.</summary>
public record ProviderInvoice(
    string Id,
    string? Status,
    string? PaymentLink,
    string? TotalAmount,
    string? Currency,
    DateTimeOffset? DueDate,
    string? CustomerName,
    string? CustomerEmail,
    string? MerchantCustomerId,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>The provider's own record of a bill as it appears in the account-wide list. The account also
/// carries bills that are not this application's, so the identifying fields here are what let eShop tell
/// its bills apart from the rest.</summary>
public record ProviderInvoiceSummary(
    string Id,
    string? Status,
    DateTimeOffset? CreatedDate,
    string? CustomerName,
    string? MerchantCustomerId,
    string? TotalAmount,
    string? Currency,
    DateTimeOffset? DueDate);
