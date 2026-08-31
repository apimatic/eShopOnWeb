using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Maps invoicing domain types onto the API DTOs.</summary>
public static class InvoiceMapping
{
    public static InvoiceDto ToDto(Invoice invoice) => new()
    {
        InvoiceId = invoice.Id,
        OrderId = invoice.OrderId,
        ProviderInvoiceId = invoice.ProviderInvoiceId,
        Status = invoice.Status.ToString(),
        ProviderStatus = invoice.ProviderStatus,
        Amount = invoice.Amount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate,
        CustomerName = invoice.Customer.Name,
        CustomerEmail = invoice.Customer.Email,
        CreatedDate = invoice.CreatedDate
    };

    public static InvoiceHistoryDto ToDto(ProviderInvoiceHistoryEntry entry) => new()
    {
        Event = entry.Event,
        Date = entry.Date
    };

    public static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        InvoiceId = entry.InvoiceId,
        ProviderInvoiceId = entry.ProviderInvoiceId,
        Status = entry.Status.ToString(),
        BelongsToEShop = entry.BelongsToEShop,
        PresentAtProvider = entry.PresentAtProvider,
        PresentInEShop = entry.PresentInEShop,
        MerchantCustomerId = entry.MerchantCustomerId,
        ProviderStatus = entry.ProviderStatus,
        EShopStatus = entry.EShopStatus?.ToString(),
        Amount = entry.Amount,
        Currency = entry.Currency,
        CreatedDate = entry.CreatedDate
    };

    public static ReconciliationSummaryDto ToDto(ReconciliationSummary summary) => new()
    {
        ProviderInvoiceCount = summary.ProviderInvoiceCount,
        EShopInvoiceCount = summary.EShopInvoiceCount,
        ReconciledCount = summary.ReconciledCount,
        MissingFromEShopCount = summary.MissingFromEShopCount,
        MissingFromProviderCount = summary.MissingFromProviderCount,
        ForeignProviderInvoiceCount = summary.ForeignProviderInvoiceCount
    };
}
