using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// A bill's current state: what eShop records about it, whatever the provider reports about how it
/// reached that state, and — once it has been put to the shopper — how they can pay it.
/// </summary>
public class InvoiceDetailsResponse : BaseResponse
{
    public InvoiceDetailsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public InvoiceDetailsResponse()
    {
    }

    /// <summary>The provider identifier for this bill; this is what the invoice endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Where the bill locally stands: Draft, Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's own status for the bill (e.g. DRAFT, SENT, CANCELED, PAID).</summary>
    public string ProviderStatus { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>How the shopper can pay the bill, present once it has been put to them.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>Whatever the provider reports about how the bill reached its current state.</summary>
    public List<string> History { get; set; } = new();

    public static InvoiceDetailsResponse From(InvoiceView view, Guid? correlationId = null)
    {
        var invoice = view.Invoice;
        var provider = view.Provider;

        var response = correlationId.HasValue
            ? new InvoiceDetailsResponse(correlationId.Value)
            : new InvoiceDetailsResponse();

        response.InvoiceId = invoice.ProviderInvoiceId;
        response.InvoiceNumber = invoice.InvoiceNumber;
        response.OrderId = invoice.OrderId;
        response.BuyerId = invoice.BuyerId;
        response.Status = invoice.Status.ToString();
        response.ProviderStatus = provider.Status;
        response.Amount = invoice.Amount;
        response.Currency = invoice.Currency;
        response.DueDate = invoice.DueDate;
        response.CustomerName = invoice.CustomerName;
        response.CustomerEmail = invoice.CustomerEmail;
        response.PaymentLink = provider.PaymentLink;
        response.History = provider.History?.ToList() ?? new List<string>();
        return response;
    }
}
