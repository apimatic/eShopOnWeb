using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>The shopper-facing view of a bill and where it has got to.</summary>
public class InvoiceDto
{
    /// <summary>The bill's identifier — the value every invoice endpoint acts on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>The application lifecycle state: Draft, Issued, or Withdrawn.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The provider's own status string for this bill, as last reported.</summary>
    public string? ProviderStatus { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static InvoiceDto From(Invoice invoice) => new()
    {
        InvoiceId = invoice.ProviderInvoiceId,
        OrderId = invoice.OrderId,
        State = invoice.State.ToString(),
        ProviderStatus = invoice.ProviderStatus,
        Amount = invoice.TotalAmount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate,
        CustomerName = invoice.CustomerName,
        CustomerEmail = invoice.CustomerEmail,
        CreatedDate = invoice.CreatedDate,
    };
}
