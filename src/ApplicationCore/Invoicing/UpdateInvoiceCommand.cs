using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A correction to a draft bill: the due date and the customer details. The amount and currency are
/// resent unchanged because the provider's update contract requires them present, but they still come
/// from the order and are never altered here.
/// </summary>
public record UpdateInvoiceCommand(
    string Description,
    DateTimeOffset DueDate,
    decimal TotalAmount,
    string Currency,
    CustomerDetails Customer,
    string MerchantCustomerId);
