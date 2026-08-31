using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single event in the provider's record of how an invoice reached its current state.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>
/// The provider's view of one invoice, as read back from a raise/get/update/send/cancel call. Only the
/// fields this integration needs are carried over; provider SDK types never leave the infrastructure layer.
/// </summary>
public record ProviderInvoice(
    string Id,
    string? Status,
    string? InvoiceNumber,
    string? PaymentLink,
    decimal? TotalAmount,
    string? Currency,
    DateTimeOffset? DueDate,
    IReadOnlyList<ProviderInvoiceEvent> History);
