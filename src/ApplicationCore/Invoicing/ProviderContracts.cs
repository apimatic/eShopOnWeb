using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single billed line, as handed to the provider gateway. The provider requires a SKU per line.</summary>
public record InvoiceLine(string Sku, string ProductName, string UnitPrice, int Quantity);

/// <summary>Everything needed to raise a brand-new (draft) bill with the provider.</summary>
public record NewInvoiceRequest(
    string Description,
    DateTimeOffset DueDate,
    string TotalAmount,
    string Currency,
    string? CustomerName,
    string? CustomerEmail,
    string? MerchantCustomerId,
    string? InvoiceNumber,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>
/// A full-replace correction of a still-draft bill. The amount and lines are re-sent unchanged
/// (the provider update is a full replace); only the due date and customer details actually move.
/// </summary>
public record InvoiceCorrection(
    string ProviderInvoiceId,
    string Description,
    DateTimeOffset DueDate,
    string TotalAmount,
    string Currency,
    string? CustomerName,
    string? CustomerEmail,
    string? MerchantCustomerId,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>What the provider returns when a bill is raised.</summary>
public record InvoiceReceipt(string ProviderInvoiceId, string? Status);

/// <summary>A single step in the provider's record of how a bill reached its current state.</summary>
public record InvoiceHistoryItem(string? Event, DateTimeOffset? Date);

/// <summary>The provider's live view of a bill.</summary>
public record InvoiceState(
    string ProviderInvoiceId,
    string? Status,
    string? PaymentLink,
    IReadOnlyList<InvoiceHistoryItem> History);

/// <summary>
/// The provider's own list-projection record of a bill, used for reconciliation. This is thinner than
/// the full record — notably it carries the merchant customer id (the one app-owned value that
/// round-trips to the list) but neither the app's invoice number nor a creation date.
/// </summary>
public record ProviderInvoiceRecord(
    string? ProviderInvoiceId,
    string? Status,
    string? MerchantCustomerId,
    string? TotalAmount,
    string? Currency);
