using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>One event in a bill's provider-owned history.</summary>
public class InvoiceEventDto
{
    public string Event { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}

/// <summary>
/// The full view of a bill returned by the detail, correct, issue and withdraw endpoints. <see cref="Status"/>
/// is eShop's lifecycle state; <see cref="ProviderStatus"/> is what Visa reports. <see cref="PaymentLink"/>
/// is a top-level field and is present only once the bill has been put to the shopper.
/// </summary>
public class InvoiceDetailsResponse : BaseResponse
{
    public InvoiceDetailsResponse(Guid correlationId) : base(correlationId) { }
    public InvoiceDetailsResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset RaisedAt { get; set; }
    public string? PaymentLink { get; set; }
    public List<InvoiceEventDto> History { get; set; } = new();

    public static InvoiceDetailsResponse From(InvoiceDetailsResult r, Guid correlationId) => new(correlationId)
    {
        InvoiceId = r.InvoiceId,
        OrderId = r.OrderId,
        Status = r.Status,
        ProviderStatus = r.ProviderStatus,
        Amount = r.Amount,
        Currency = r.Currency,
        DueDate = r.DueDate,
        CustomerName = r.CustomerName,
        CustomerEmail = r.CustomerEmail,
        RaisedAt = r.RaisedAt,
        PaymentLink = r.PaymentLink,
        History = r.History.Select(h => new InvoiceEventDto { Event = h.Event, Date = h.Date }).ToList()
    };
}

/// <summary>A compact view of a bill for the shopper's list.</summary>
public class InvoiceSummaryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset RaisedAt { get; set; }

    public static InvoiceSummaryDto From(InvoiceSummaryResult r) => new()
    {
        InvoiceId = r.InvoiceId,
        OrderId = r.OrderId,
        Status = r.Status,
        Amount = r.Amount,
        Currency = r.Currency,
        DueDate = r.DueDate,
        RaisedAt = r.RaisedAt
    };
}

/// <summary>One line of the reconciliation report.</summary>
public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Which side(s) know about the bill: RecordedByBoth, ProviderOnly, or EShopOnly.</summary>
    public string Source { get; set; } = string.Empty;

    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        InvoiceId = e.InvoiceId,
        Source = e.Source.ToString(),
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        OrderId = e.OrderId,
        Amount = e.Amount,
        Currency = e.Currency,
        CustomerName = e.CustomerName,
        CreatedDate = e.CreatedDate
    };
}
