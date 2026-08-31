using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>A single billed line on an invoice.</summary>
public class InvoiceLineDto
{
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>A step in how a bill reached its current state, as the provider reports it.</summary>
public class InvoiceEventDto
{
    public string Event { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}

/// <summary>The full detail of a bill.</summary>
public class InvoiceDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    /// <summary>eShop's local lifecycle: Draft, Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>The richer status the provider owns: DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED...</summary>
    public string ProviderStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>How the bill can be paid; present only once it has been put to the shopper.</summary>
    public string? PaymentLink { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public List<InvoiceEventDto> History { get; set; } = new();
}

/// <summary>A shopper's bill in a list, showing where it has got to.</summary>
public class InvoiceSummaryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}

/// <summary>One reconciled bill, lined up between the provider and eShop.</summary>
public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    /// <summary>Both, ProviderOnly or EShopOnly.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Whether this bill is one of this application's, or another activity's on the shared account.</summary>
    public bool BelongsToEShop { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? RaisedAt { get; set; }
    public string? CustomerName { get; set; }
    public int? OrderId { get; set; }
}

/// <summary>The operator reconciliation report over a date range.</summary>
public class ReconciliationReportDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>Maps application-core invoicing views onto the API DTOs.</summary>
public static class InvoiceDtoMapper
{
    public static InvoiceDto ToDto(InvoiceDetailView v) => new()
    {
        InvoiceId = v.InvoiceId,
        OrderId = v.OrderId,
        BuyerId = v.BuyerId,
        Status = v.LocalStatus,
        ProviderStatus = v.ProviderStatus,
        Amount = v.Amount,
        Currency = v.Currency,
        DueDate = v.DueDate,
        CustomerName = v.CustomerName,
        CustomerEmail = v.CustomerEmail,
        Description = v.Description,
        PaymentLink = v.PaymentLink,
        Lines = v.Lines.Select(l => new InvoiceLineDto
        {
            ProductSku = l.ProductSku,
            ProductName = l.ProductName,
            Units = l.Units,
            UnitPrice = l.UnitPrice,
            LineTotal = l.LineTotal
        }).ToList(),
        History = v.History.Select(h => new InvoiceEventDto { Event = h.Event, Date = h.Date }).ToList()
    };

    public static InvoiceSummaryDto ToDto(InvoiceSummaryView v) => new()
    {
        InvoiceId = v.InvoiceId,
        OrderId = v.OrderId,
        Status = v.LocalStatus,
        ProviderStatus = v.ProviderStatus,
        Amount = v.Amount,
        Currency = v.Currency,
        DueDate = v.DueDate
    };

    public static ReconciliationReportDto ToDto(ReconciliationReport r) => new()
    {
        From = r.From,
        To = r.To,
        ProviderCount = r.ProviderCount,
        EShopCount = r.EShopCount,
        MatchedCount = r.MatchedCount,
        ProviderOnlyCount = r.ProviderOnlyCount,
        EShopOnlyCount = r.EShopOnlyCount,
        Entries = r.Entries.Select(e => new ReconciliationEntryDto
        {
            InvoiceId = e.InvoiceId,
            Source = e.Source.ToString(),
            BelongsToEShop = e.BelongsToEShop,
            ProviderStatus = e.ProviderStatus,
            LocalStatus = e.LocalStatus,
            Amount = e.Amount,
            Currency = e.Currency,
            DueDate = e.DueDate,
            RaisedAt = e.RaisedAt,
            CustomerName = e.CustomerName,
            OrderId = e.OrderId
        }).ToList()
    };
}
