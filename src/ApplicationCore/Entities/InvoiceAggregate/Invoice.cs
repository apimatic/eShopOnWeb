using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider.
/// The invoice owns just enough of the provider-side state (its identifier there and the lifecycle
/// stage eShop drives) that a later request can act on it and report on it, not only the one that
/// raised it. What is billed — the amount — is derived from the order and is never editable here.
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
        string invoiceNumber,
        string customerName,
        string customerEmail,
        decimal amount,
        string currencyCode,
        DateOnly dueDate)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(invoiceNumber, nameof(invoiceNumber));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.Negative(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        InvoiceNumber = invoiceNumber;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Amount = amount;
        CurrencyCode = currencyCode;
        DueDate = dueDate;
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order, and therefore the bill. Used for access scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this invoice; the handle for every later provider call.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The human-facing invoice number submitted to the provider (also the provider id).</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>Payer name carried on the bill; correctable while the bill is still a draft.</summary>
    public string CustomerName { get; private set; }

    /// <summary>Payer email carried on the bill; correctable while the bill is still a draft.</summary>
    public string CustomerEmail { get; private set; }

    /// <summary>The billed amount, derived from the order. Not editable on the invoice.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency the bill is raised in (this account bills in USD).</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The calendar date the bill falls due; correctable while still a draft.</summary>
    public DateOnly DueDate { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>
    /// Correct the due date and customer details a draft bill carries. Refused once the bill has
    /// been put to the shopper or withdrawn — the caller is told rather than the change no-op'ing.
    /// </summary>
    public void ApplyCorrection(DateOnly? dueDate, string? customerName, string? customerEmail)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvoiceStateException(
                $"Invoice {Id} cannot be corrected because it has already been {Status.ToString().ToLowerInvariant()}. " +
                "Only a bill that has not yet been put to the shopper can be corrected.");
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

    /// <summary>Record that the bill has been put to the shopper. Only a draft can be issued.</summary>
    public void MarkIssued()
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {Id} has been withdrawn and cannot be put to the shopper.");
        }

        if (Status == InvoiceStatus.Issued)
        {
            throw new InvoiceStateException($"Invoice {Id} has already been put to the shopper.");
        }

        Status = InvoiceStatus.Issued;
        IssuedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the bill has been withdrawn. A withdrawn bill cannot be withdrawn again.</summary>
    public void MarkWithdrawn()
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {Id} has already been withdrawn.");
        }

        Status = InvoiceStatus.Withdrawn;
        WithdrawnAt = DateTimeOffset.UtcNow;
    }
}
