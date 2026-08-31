using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>A step in the provider's record of how a bill reached its current state.</summary>
public class InvoiceHistoryEntryDto
{
    public string Event { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}

/// <summary>A bill as listed for the shopper, showing where it has got to.</summary>
public class MyInvoiceDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }

    /// <summary>eShop's lifecycle state (Draft/Issued/Withdrawn).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Last known status reported by the provider.</summary>
    public string ProviderStatus { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public bool Payable { get; set; }
}

/// <summary>A single bill lined up across the provider's record and eShop's record.</summary>
public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Which side(s) know about the bill: Both, ProviderOnly, or EShopOnly.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when eShop raised this bill; false when it belongs to other activity on the account.</summary>
    public bool IsEShopInvoice { get; set; }

    public string? ProviderStatus { get; set; }
    public string? EShopState { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerName { get; set; }
    public int? OrderId { get; set; }
}

internal static class InvoiceMappings
{
    public static MyInvoiceDto ToMyInvoiceDto(Invoice invoice) => new()
    {
        InvoiceId = invoice.ProviderInvoiceId,
        OrderId = invoice.OrderId,
        State = invoice.LifecycleState.ToString(),
        ProviderStatus = invoice.ProviderStatus,
        Amount = invoice.TotalAmount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate,
        CreatedDate = invoice.CreatedDate,
        Payable = invoice.LifecycleState == InvoiceLifecycleState.Issued
    };

    public static List<InvoiceHistoryEntryDto> ToHistory(ProviderInvoice provider) =>
        provider.History
            .Select(h => new InvoiceHistoryEntryDto { Event = h.Event, Date = h.Date })
            .ToList();

    public static ReconciliationEntryDto ToReconciliationDto(ReconciliationEntry entry) => new()
    {
        InvoiceId = entry.InvoiceId,
        Source = entry.Source.ToString(),
        IsEShopInvoice = entry.Source != ReconciliationSource.ProviderOnly,
        ProviderStatus = entry.ProviderStatus,
        EShopState = entry.EShopStatus,
        CreatedDate = entry.CreatedDate,
        Amount = entry.Amount,
        Currency = entry.Currency,
        CustomerName = entry.CustomerName,
        OrderId = entry.OrderId
    };
}
