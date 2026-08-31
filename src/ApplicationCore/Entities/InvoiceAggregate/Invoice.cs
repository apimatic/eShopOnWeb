using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop's local record of a bill raised against an <see cref="OrderAggregate.Order"/> with the
/// billing provider. It carries enough of the state the provider owns — the provider's invoice id
/// and the last-known lifecycle position — that a later request can act on and report the bill,
/// not only the one that raised it. The amount is never stored as correctable data: it is derived
/// from the order and only snapshotted here for display/reconciliation.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }

    public Invoice(int orderId, string buyerId, string providerInvoiceId, DateTimeOffset dueDate,
        string? customerName, string? customerEmail, string currency, decimal amount)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Currency = currency;
        Amount = amount;
        Status = InvoiceStatus.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (the order's buyer). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for this invoice — the identity every later provider call uses.</summary>
    public string ProviderInvoiceId { get; private set; }

    public DateTimeOffset DueDate { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerEmail { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Snapshot of the order total at raise time, for display/reconciliation only.</summary>
    public decimal Amount { get; private set; }

    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>A bill can only be corrected while it is still a draft (not yet put to the shopper, not withdrawn).</summary>
    public bool CanBeCorrected => Status == InvoiceStatus.Draft;

    /// <summary>Correct the due date and/or customer details on a still-draft bill. The amount is not correctable.</summary>
    public void ApplyCorrection(DateTimeOffset? dueDate, string? customerName, string? customerEmail)
    {
        if (!CanBeCorrected)
        {
            throw new InvalidOperationException(
                $"Invoice {ProviderInvoiceId} can no longer be corrected because it is {Status}.");
        }

        if (dueDate.HasValue)
        {
            DueDate = dueDate.Value;
        }

        if (customerName is not null)
        {
            CustomerName = customerName;
        }

        if (customerEmail is not null)
        {
            CustomerEmail = customerEmail;
        }
    }

    /// <summary>Mark the bill as put to the shopper.</summary>
    public void MarkIssued() => Status = InvoiceStatus.Issued;

    /// <summary>Mark the bill as withdrawn — it is no longer payable.</summary>
    public void MarkWithdrawn() => Status = InvoiceStatus.Withdrawn;
}
