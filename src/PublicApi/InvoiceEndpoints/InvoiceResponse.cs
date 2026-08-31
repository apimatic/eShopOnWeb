using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// The shape returned by every endpoint that yields a single bill (raise, get, correct, issue,
/// withdraw). <see cref="InvoiceId"/> and — once the bill is issued and still payable —
/// <see cref="PaymentLink"/> are top-level fields, as the flow requires.
/// </summary>
public class InvoiceResponse : BaseResponse
{
    public InvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public InvoiceResponse()
    {
    }

    /// <summary>The provider's invoice identifier — the id every later request acts on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>eShop's local lifecycle status: Draft, Issued or Withdrawn.</summary>
    public string LocalStatus { get; set; } = string.Empty;

    /// <summary>The provider's authoritative status string, as last read from the provider.</summary>
    public string? ProviderStatus { get; set; }

    public DateTimeOffset DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>How the shopper can pay the bill. Present only once issued and still payable.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's account of how the bill reached its current state.</summary>
    public List<InvoiceHistoryItem> History { get; set; } = new();

    public static InvoiceResponse From(InvoiceDetails details, Guid correlationId) => new(correlationId)
    {
        InvoiceId = details.InvoiceId,
        OrderId = details.OrderId,
        LocalStatus = details.LocalStatus,
        ProviderStatus = details.ProviderStatus,
        DueDate = details.DueDate,
        CustomerName = details.CustomerName,
        CustomerEmail = details.CustomerEmail,
        Currency = details.Currency,
        Amount = details.Amount,
        PaymentLink = details.PaymentLink,
        History = details.History
            .Select(h => new InvoiceHistoryItem { Event = h.Event, Date = h.Date })
            .ToList()
    };
}

public class InvoiceHistoryItem
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}
