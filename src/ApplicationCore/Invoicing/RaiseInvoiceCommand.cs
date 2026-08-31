using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Everything the provider needs to raise a bill. Built entirely from the order and the requested due
/// date — the caller never restates what is billed.
/// </summary>
public record RaiseInvoiceCommand(
    string InvoiceNumber,
    string Description,
    DateTimeOffset DueDate,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<InvoiceLineItem> LineItems,
    CustomerDetails Customer,
    string MerchantCustomerId);
