using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Customer details a bill is addressed to.</summary>
public class CustomerDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>A single event in the provider's account of how a bill reached its current state.</summary>
public class InvoiceEventDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}

/// <summary>The full view of a bill, returned by raise / get / correct / issue / withdraw.</summary>
public class InvoiceResponse
{
    /// <summary>The provider's identifier for the bill — the id every invoice endpoint acts on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>The provider's status for the bill (DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED, ...).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether the bill has been put to the shopper (issued and not withdrawn).</summary>
    public bool Issued { get; set; }

    /// <summary>Whether the bill has been withdrawn.</summary>
    public bool Withdrawn { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public CustomerDto? Customer { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// How the shopper can pay the bill, once it has been put to them. Top-level, and null until the
    /// bill is issued (and again null once withdrawn).
    /// </summary>
    public string? PaymentLink { get; set; }

    public DateTimeOffset? ProviderSubmittedUtc { get; set; }

    /// <summary>Whatever the provider reports about how the bill reached its current state.</summary>
    public IEnumerable<InvoiceEventDto>? ProviderHistory { get; set; }

    public static InvoiceResponse From(InvoiceSnapshot snapshot)
    {
        var local = snapshot.Local;
        var provider = snapshot.Provider;
        var status = string.IsNullOrEmpty(provider.Status) ? local.Status : provider.Status;

        return new InvoiceResponse
        {
            InvoiceId = local.ProviderInvoiceId,
            OrderId = local.OrderId,
            Status = status,
            Issued = InvoiceStatus.IsIssued(status),
            Withdrawn = InvoiceStatus.IsWithdrawn(status),
            Amount = provider.Amount ?? local.Amount,
            Currency = provider.Currency ?? local.Currency,
            DueDate = provider.DueDate ?? local.DueDate,
            Customer = new CustomerDto
            {
                Name = provider.CustomerName ?? local.CustomerName,
                Email = provider.CustomerEmail ?? local.CustomerEmail
            },
            Description = provider.Description,
            // A withdrawn bill must no longer hand out a way to pay it.
            PaymentLink = InvoiceStatus.IsWithdrawn(status) ? null : provider.PaymentLink,
            ProviderSubmittedUtc = provider.SubmittedUtc,
            ProviderHistory = provider.History.Select(h => new InvoiceEventDto { Event = h.Event, Date = h.Date })
        };
    }
}

/// <summary>A compact view of a bill for the caller's list of bills.</summary>
public class InvoiceSummaryResponse
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Issued { get; set; }
    public bool Withdrawn { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }

    public static InvoiceSummaryResponse From(Invoice invoice) => new()
    {
        InvoiceId = invoice.ProviderInvoiceId,
        OrderId = invoice.OrderId,
        Status = invoice.Status,
        Issued = InvoiceStatus.IsIssued(invoice.Status),
        Withdrawn = InvoiceStatus.IsWithdrawn(invoice.Status),
        Amount = invoice.Amount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate
    };
}
