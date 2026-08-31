using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Full detail of a bill, returned by GET/PATCH/issue/withdraw.</summary>
public class InvoiceResponse : BaseResponse
{
    public InvoiceResponse(Guid correlationId) : base(correlationId) { }

    public InvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool PutToShopper { get; set; }
    public bool Withdrawn { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How the bill can be paid — present only once it has been put to the shopper.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's own account of how the bill reached its current state.</summary>
    public List<InvoiceEventDto> ProviderHistory { get; set; } = new();
}

public class InvoiceEventDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public RaiseInvoiceResponse() { }

    /// <summary>The provider identifier for the newly raised bill.</summary>
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId) { }

    public MyInvoicesResponse() { }

    public List<InvoiceSummaryDto> Invoices { get; set; } = new();
}

public class InvoiceSummaryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool PutToShopper { get; set; }
    public bool Withdrawn { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? PaymentLink { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderInvoiceCount { get; set; }
    public int EShopInvoiceCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Matched, ProviderOnly (not this application's bill), or EShopOnly.</summary>
    public string Match { get; set; } = string.Empty;

    public bool BelongsToEShop { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public string? BuyerId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>Maps application view models to API response shapes.</summary>
internal static class InvoiceDtoMapper
{
    public static InvoiceResponse ToResponse(InvoiceDetails details, Guid correlationId) => new(correlationId)
    {
        InvoiceId = details.InvoiceId,
        OrderId = details.OrderId,
        Status = details.Status,
        PutToShopper = details.PutToShopper,
        Withdrawn = details.Withdrawn,
        Amount = details.Amount,
        Currency = details.Currency,
        Description = details.Description,
        CustomerName = details.CustomerName,
        CustomerEmail = details.CustomerEmail,
        DueDate = details.DueDate,
        CreatedAt = details.CreatedAt,
        PaymentLink = details.PaymentLink,
        ProviderHistory = details.ProviderHistory
            .Select(e => new InvoiceEventDto { Event = e.Event, Date = e.Date })
            .ToList()
    };

    public static InvoiceSummaryDto ToSummaryDto(InvoiceSummaryView view) => new()
    {
        InvoiceId = view.InvoiceId,
        OrderId = view.OrderId,
        Status = view.Status,
        PutToShopper = view.PutToShopper,
        Withdrawn = view.Withdrawn,
        Amount = view.Amount,
        Currency = view.Currency,
        DueDate = view.DueDate,
        CustomerName = view.CustomerName,
        PaymentLink = view.PaymentLink
    };

    public static ReconciliationResponse ToResponse(ReconciliationReport report, Guid correlationId) => new(correlationId)
    {
        From = report.From,
        To = report.To,
        ProviderInvoiceCount = report.ProviderInvoiceCount,
        EShopInvoiceCount = report.EShopInvoiceCount,
        MatchedCount = report.MatchedCount,
        ProviderOnlyCount = report.ProviderOnlyCount,
        EShopOnlyCount = report.EShopOnlyCount,
        Entries = report.Entries.Select(e => new ReconciliationEntryDto
        {
            InvoiceId = e.InvoiceId,
            Match = e.Match.ToString(),
            BelongsToEShop = e.BelongsToEShop,
            ProviderStatus = e.ProviderStatus,
            EShopStatus = e.EShopStatus,
            OrderId = e.OrderId,
            BuyerId = e.BuyerId,
            Amount = e.Amount,
            Currency = e.Currency,
            CreatedDate = e.CreatedDate,
            CustomerName = e.CustomerName
        }).ToList()
    };
}
