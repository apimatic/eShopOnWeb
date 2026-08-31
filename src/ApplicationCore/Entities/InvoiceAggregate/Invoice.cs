using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop's local record of a bill raised against an <see cref="OrderAggregate.Order"/> with the
/// invoicing provider (Visa / CyberSource). It carries enough of the state the provider owns — the
/// provider's identifier for the bill and where the bill currently stands — that a later request can
/// act on it and report on it, independently of the request that raised it.
///
/// The amount is a snapshot of the order total at the time the bill was raised; what is billed always
/// comes from the order, never from a caller restating it.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        string status,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string customerName,
        string customerEmail)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Status = status;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order and therefore the bill.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this bill — the handle every later provider call uses.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>Last-known status as reported by the provider (DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED, ...).</summary>
    public string Status { get; private set; }

    /// <summary>The billed amount — snapshot of the order total when the bill was raised.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Record the latest status the provider reported for this bill.</summary>
    public void SyncStatus(string status)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
    }

    /// <summary>
    /// Apply a correction to the mutable, order-independent details of the bill (due date and the
    /// customer the bill is addressed to). The billed amount is intentionally not correctable here.
    /// </summary>
    public void ApplyCorrection(DateOnly dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }
}
