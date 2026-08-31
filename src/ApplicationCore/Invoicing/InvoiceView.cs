using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A read model for a single bill: this application's lifecycle state combined with what the provider
/// currently reports (its status, how it got there, and — once the bill has been put to the shopper —
/// the way to pay it). The payment link is already gated: it is only present once issued and is cleared
/// again once withdrawn.
/// </summary>
public record InvoiceView(
    string InvoiceId,
    int OrderId,
    string BuyerId,
    string InvoiceNumber,
    InvoiceState State,
    string? ProviderStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset DueDate,
    string CustomerName,
    string CustomerEmail,
    string? PaymentLink,
    IReadOnlyList<ProviderInvoiceEvent> History);
