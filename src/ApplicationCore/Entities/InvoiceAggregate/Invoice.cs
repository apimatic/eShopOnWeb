using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment
/// provider (Visa / CyberSource). This entity is eShop's local record of a provider-owned
/// invoice: it keeps the provider's identifier and last-known status so later requests can
/// act on and report about the bill, not only the request that raised it.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    // Provider invoice statuses (as reported by CyberSource Invoicing).
    public const string StatusDraft = "DRAFT";
    public const string StatusCreated = "CREATED";
    public const string StatusSent = "SENT";
    public const string StatusPartial = "PARTIAL";
    public const string StatusPaid = "PAID";
    public const string StatusCanceled = "CANCELED";

#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(
        int orderId,
        string buyerId,
        string providerInvoiceId,
        decimal amount,
        string currency,
        DateTime dueDate,
        string customerName,
        string customerEmail,
        string status)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper that owns this bill (order buyer id / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for this invoice.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The billed amount. Sourced from the order, never restated by the caller.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTime DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>Last known provider status (DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// A bill can be corrected only while it has not yet been put to the shopper and has not
    /// been withdrawn.
    /// </summary>
    public bool IsCorrectable =>
        string.Equals(Status, StatusDraft, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, StatusCreated, StringComparison.OrdinalIgnoreCase);

    public bool IsWithdrawn =>
        string.Equals(Status, StatusCanceled, StringComparison.OrdinalIgnoreCase);

    /// <summary>The bill has been put to the shopper (or beyond, i.e. partially/fully paid).</summary>
    public bool IsIssued =>
        string.Equals(Status, StatusSent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, StatusPartial, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, StatusPaid, StringComparison.OrdinalIgnoreCase);

    public void SyncStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }
    }

    public void ApplyCorrection(DateTime dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }
}
