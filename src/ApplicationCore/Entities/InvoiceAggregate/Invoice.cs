using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the invoicing
/// provider (Visa/CyberSource). eShop persists just enough to link its own record to the
/// provider's — the provider invoice id and where the bill currently stands — so that a later
/// request can act on it and report on it, not only the request that raised it. What is billed
/// (the amount and its line items) always comes from the order and is never restated here.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
    #pragma warning restore CS8618

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        string currencyCode,
        DateOnly dueDate,
        decimal amount,
        string status,
        string? customerName,
        string? customerEmail)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        CurrencyCode = currencyCode;
        DueDate = dueDate;
        Amount = amount;
        Status = status;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (and therefore this bill).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for this invoice. Also the public invoiceId.</summary>
    public string ProviderInvoiceId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The calendar date the bill falls due. Correctable while the bill is a draft.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Snapshot of the order total at the time the bill was raised, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Last known provider status. Refreshed from the provider whenever the bill is read.</summary>
    public string Status { get; private set; }

    public string? CustomerName { get; private set; }

    public string? CustomerEmail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True while the bill has not yet been put to the shopper.</summary>
    public bool IsDraft =>
        string.Equals(Status, InvoiceStatus.Draft, StringComparison.OrdinalIgnoreCase);

    /// <summary>True once the bill has been withdrawn.</summary>
    public bool IsWithdrawn =>
        string.Equals(Status, InvoiceStatus.Canceled, StringComparison.OrdinalIgnoreCase);

    /// <summary>Records the provider's current view of where this bill has got to.</summary>
    public void SyncStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }
    }

    /// <summary>
    /// Applies a correction to the customer-facing details a draft bill carries. The billed
    /// amount is deliberately not a parameter: it always comes from the order.
    /// </summary>
    public void ApplyCorrection(DateOnly dueDate, string? customerName, string? customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }
}
