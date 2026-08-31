using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// The full current state of a bill, returned by reading, correcting, issuing and withdrawing it.
/// Blends eShop's own record with whatever the provider reports about how the bill got here.
/// </summary>
public class InvoiceResponse : BaseResponse
{
    public InvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public InvoiceResponse()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public bool IsIssued { get; set; }
    public bool IsWithdrawn { get; set; }

    /// <summary>How the shopper can pay the bill; present only once it has been issued and not withdrawn.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>What the provider reports about how the bill reached its current state.</summary>
    public List<InvoiceHistoryDto> History { get; set; } = new();

    public static InvoiceResponse From(InvoiceDetail detail, Guid correlationId) => new(correlationId)
    {
        InvoiceId = detail.InvoiceId,
        OrderId = detail.OrderId,
        Status = detail.Status,
        Amount = detail.Amount,
        Currency = detail.Currency,
        DueDate = detail.DueDate,
        CustomerName = detail.CustomerName,
        CustomerEmail = detail.CustomerEmail,
        IsIssued = detail.IsIssued,
        IsWithdrawn = detail.IsWithdrawn,
        PaymentLink = detail.PaymentLink,
        History = detail.History
            .Select(h => new InvoiceHistoryDto { Event = h.Event, Date = h.Date })
            .ToList()
    };
}

public class InvoiceHistoryDto
{
    public string Event { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}
