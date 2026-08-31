using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>A single step in the provider's account of how a bill reached its current state.</summary>
public class InvoiceEventDto
{
    public string Event { get; set; } = string.Empty;
    public DateTimeOffset? At { get; set; }
}

/// <summary>A shopper's own bill as it appears in their list of bills.</summary>
public class InvoiceListItemDto
{
    public int InvoiceId { get; set; }
    public int OrderId { get; set; }
    public string ProviderInvoiceId { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Where the bill has got to, as eShop drives it: Draft, Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

/// <summary>Static mappers from domain shapes to API DTOs.</summary>
public static class InvoiceMapping
{
    public static InvoiceListItemDto ToListItem(Invoice invoice) => new()
    {
        InvoiceId = invoice.Id,
        OrderId = invoice.OrderId,
        ProviderInvoiceId = invoice.ProviderInvoiceId,
        InvoiceNumber = invoice.InvoiceNumber,
        Status = invoice.Status.ToString(),
        Amount = invoice.Amount,
        Currency = invoice.CurrencyCode,
        DueDate = invoice.DueDate,
        CreatedAt = invoice.CreatedAt,
        IssuedAt = invoice.IssuedAt,
        WithdrawnAt = invoice.WithdrawnAt
    };

    public static List<InvoiceEventDto> ToEventDtos(IReadOnlyList<ProviderInvoiceEvent> history) =>
        history.Select(h => new InvoiceEventDto { Event = h.Event, At = h.At }).ToList();
}
