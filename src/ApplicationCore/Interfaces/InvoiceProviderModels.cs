using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line the bill is composed of, as sent to the provider.</summary>
public record ProviderLineItem(string Sku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>Everything the provider needs to raise a new (draft) bill.</summary>
public record ProviderInvoiceDraft(
    string InvoiceNumber,
    string Description,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string MerchantReference,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyList<ProviderLineItem> LineItems);

/// <summary>
/// The correctable fields on a bill. The amount and line items are still sent (the provider requires
/// them on update) but they are always the order's own figures, never anything the caller restated.
/// </summary>
public record ProviderInvoiceUpdate(
    string InvoiceNumber,
    string Description,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    string MerchantReference,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyList<ProviderLineItem> LineItems);

/// <summary>The provider's handle for a bill plus the status it reports right after an action.</summary>
public record ProviderInvoiceRef(string ProviderInvoiceId, string InvoiceNumber, string Status);

/// <summary>A single entry in the provider's own account of how a bill reached its current state.</summary>
public record ProviderInvoiceEvent(string Event, DateTimeOffset? At);

/// <summary>The full provider-side view of a bill, read back on demand.</summary>
public record ProviderInvoiceDetails(
    string ProviderInvoiceId,
    string InvoiceNumber,
    string Status,
    string? PaymentLink,
    decimal? Amount,
    string? CurrencyCode,
    DateOnly? DueDate,
    string? CustomerName,
    string? CustomerEmail,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>
/// A provider bill as it appears in the provider's own list, together with the date it was raised
/// (resolved from the bill's history). Used to reconcile the provider's record against eShop's.
/// </summary>
public record ProviderInvoiceSummary(
    string ProviderInvoiceId,
    string InvoiceNumber,
    string Status,
    decimal? Amount,
    string? CurrencyCode,
    string? CustomerName,
    DateTimeOffset? RaisedAt);
