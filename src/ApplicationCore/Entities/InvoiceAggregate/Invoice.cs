using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider.
/// eShop keeps only the state a later request needs to act on and report the bill: the provider's
/// identifier for it, where it stands in eShop's own lifecycle, and the billed facts (which come
/// from the order, never from the caller). The authoritative payment state always lives with the
/// provider and is fetched on demand.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string customerName,
        string customerEmail)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Status = InvoiceStatus.Raised;
        RaisedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order, and therefore the bill.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this bill; what later provider calls act on.</summary>
    public string ProviderInvoiceId { get; private set; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>Billed amount, taken from the order. Not correctable here.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>True once the bill has been put to the shopper.</summary>
    public bool HasBeenIssued => Status == InvoiceStatus.Issued;

    /// <summary>True once the bill has been withdrawn.</summary>
    public bool HasBeenWithdrawn => Status == InvoiceStatus.Withdrawn;

    /// <summary>
    /// Correct the due date and customer details while the bill has not yet been put to the shopper.
    /// The billed amount is never corrected here.
    /// </summary>
    public void Correct(DateOnly dueDate, string customerName, string customerEmail)
    {
        if (Status != InvoiceStatus.Raised)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {ProviderInvoiceId} cannot be corrected because it has been " +
                $"{(Status == InvoiceStatus.Issued ? "put to the shopper" : "withdrawn")}.");
        }

        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Put the bill to the shopper.</summary>
    public void MarkIssued()
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {ProviderInvoiceId} has been withdrawn and cannot be put to the shopper.");
        }

        if (Status == InvoiceStatus.Issued)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {ProviderInvoiceId} has already been put to the shopper.");
        }

        Status = InvoiceStatus.Issued;
    }

    /// <summary>Withdraw the bill so it can no longer be paid.</summary>
    public void MarkWithdrawn()
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {ProviderInvoiceId} has already been withdrawn.");
        }

        Status = InvoiceStatus.Withdrawn;
    }
}
