using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop's record of a bill raised against an <see cref="OrderAggregate.Order"/> with the payment
/// provider (Visa / CyberSource). It carries enough of the state the provider owns — the invoice's
/// identifier there and where it currently stands — that a later request can act on and report on
/// the bill, not just the request that raised it.
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
        DateOnly dueDate,
        decimal amount,
        string currency,
        string customerName,
        string customerEmail,
        string providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        DueDate = dueDate;
        Amount = amount;
        Currency = currency;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderStatus = providerStatus;
        State = InvoiceState.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper the bill belongs to (the order's buyer). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The invoice identifier assigned by the provider. This is the public <c>invoiceId</c>.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>eShop's own lifecycle stage for the bill.</summary>
    public InvoiceState State { get; private set; }

    /// <summary>The last status the provider reported (DRAFT / CREATED / SENT / CANCELED / ...).</summary>
    public string ProviderStatus { get; private set; }

    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerEmail { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>
    /// A bill can only be corrected while it is still a draft — i.e. before it has been put to the
    /// shopper and before it has been withdrawn.
    /// </summary>
    public bool CanBeCorrected => State == InvoiceState.Draft;

    /// <summary>Whether a payment link may still be handed out for this bill.</summary>
    public bool IsPayable => State == InvoiceState.Issued;

    /// <summary>Record the correctable customer/due-date fields after a successful provider update.</summary>
    public void ApplyCorrection(DateOnly dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Record that the bill has been put to the shopper.</summary>
    public void MarkIssued(string providerStatus)
    {
        State = InvoiceState.Issued;
        ProviderStatus = providerStatus;
    }

    /// <summary>Record that the bill has been withdrawn and is no longer payable.</summary>
    public void MarkWithdrawn(string providerStatus)
    {
        State = InvoiceState.Withdrawn;
        ProviderStatus = providerStatus;
    }

    /// <summary>Refresh the mirrored provider status without changing eShop's own lifecycle stage.</summary>
    public void SyncProviderStatus(string? providerStatus)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }
}
