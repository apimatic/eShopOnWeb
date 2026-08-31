using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the
/// payment provider. The aggregate carries enough of the provider-owned state —
/// the provider's identifier for the bill and where it currently stands — that a
/// later request can act on and report the bill, not only the request that raised it.
///
/// What is billed (the lines and their amount) is snapshotted from the order at
/// raise time and is never re-stated by a caller, so corrections can change the
/// due date and customer details but never the amount.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(
        int orderId,
        string buyerId,
        string providerInvoiceId,
        string providerInvoiceNumber,
        string description,
        string currencyCode,
        DateOnly dueDate,
        string customerName,
        string customerEmail,
        string providerStatus,
        List<InvoiceItem> items)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.Null(items, nameof(items));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        ProviderInvoiceNumber = providerInvoiceNumber;
        Description = description;
        CurrencyCode = currencyCode;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderStatus = providerStatus;
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
        _items = items;
    }

    /// <summary>The order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (their order's buyer id / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for this bill. This is the public invoice id.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The provider's invoice number.</summary>
    public string ProviderInvoiceNumber { get; private set; }

    /// <summary>eShop's local view of the bill's lifecycle.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>The last status the provider reported (DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED...).</summary>
    public string ProviderStatus { get; private set; }

    public string Description { get; private set; }
    public string CurrencyCode { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerEmail { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<InvoiceItem> _items = new List<InvoiceItem>();
    public IReadOnlyCollection<InvoiceItem> Items => _items.AsReadOnly();

    /// <summary>The billed amount, computed from the snapshotted lines (never restated by a caller).</summary>
    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _items)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    /// <summary>True while the bill has not been put to the shopper nor withdrawn.</summary>
    public bool CanBeAmended => Status == InvoiceStatus.Draft;

    /// <summary>
    /// Corrects the due date and customer details the bill carries. Only permitted
    /// before the bill has been put to the shopper or withdrawn.
    /// </summary>
    public void ApplyCorrection(DateOnly dueDate, string customerName, string customerEmail)
    {
        if (!CanBeAmended)
        {
            throw new InvalidOperationException(
                $"Invoice {ProviderInvoiceId} can no longer be corrected because it is {Status}.");
        }

        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Marks the bill as put to the shopper.</summary>
    public void MarkIssued(string providerStatus)
    {
        Status = InvoiceStatus.Issued;
        SyncProviderStatus(providerStatus);
    }

    /// <summary>Marks the bill as withdrawn.</summary>
    public void MarkWithdrawn(string providerStatus)
    {
        Status = InvoiceStatus.Withdrawn;
        SyncProviderStatus(providerStatus);
    }

    /// <summary>Refreshes the cached provider status without changing the local lifecycle.</summary>
    public void SyncProviderStatus(string providerStatus)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }
}
