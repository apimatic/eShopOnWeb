using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// One record from the provider's list of invoices, used for reconciliation. The list does not return
/// an invoice number, so <see cref="MerchantCustomerId"/> is the only field that identifies which
/// invoices this application raised.
/// </summary>
public record ProviderInvoiceSummary(
    string Id,
    string? Status,
    DateTimeOffset? CreatedDate,
    decimal? TotalAmount,
    string? Currency,
    string? MerchantCustomerId,
    string? CustomerName);
