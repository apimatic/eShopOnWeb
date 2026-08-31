using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the Visa/CyberSource
/// provider. eShop keeps enough of the provider-owned state (the provider's identifier and where the
/// bill stands) that a later request can act on and report the bill, not only the one that raised it.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
    #pragma warning restore CS8618

    public Invoice(int orderId, string buyerId, string providerInvoiceId, string invoiceNumber,
        string merchantReference, decimal amount, string currency, DateTimeOffset dueDate,
        string? customerName, string? customerEmail)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        InvoiceNumber = invoiceNumber;
        MerchantReference = merchantReference;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Status = InvoiceStatus.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (their username). Only they — or an operator — may act on it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for the bill. This is the public invoice id the API acts on.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>An app-owned invoice number stamped on the bill for audit/detail lookups.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>App-owned marker written to the provider so eShop-originated bills are recognisable at list time.</summary>
    public string MerchantReference { get; private set; }

    /// <summary>The billed amount, snapshotted from the order. Never editable through a correction.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }
    public DateTimeOffset DueDate { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerEmail { get; private set; }
    public InvoiceStatus Status { get; private set; }

    /// <summary>The way to pay the bill, once it has been put to the shopper. Cleared when withdrawn.</summary>
    public string? PaymentLink { get; private set; }

    /// <summary>The last raw status string the provider reported, for reference.</summary>
    public string? ProviderStatus { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public bool IsDraft => Status == InvoiceStatus.Draft;
    public bool IsIssued => Status == InvoiceStatus.Issued;
    public bool IsWithdrawn => Status == InvoiceStatus.Withdrawn;

    /// <summary>
    /// Correct the due date and customer details. Permitted only while the bill is still a draft;
    /// once it has been put to the shopper or withdrawn, correcting it is refused. The amount is never
    /// corrected here — it comes from the order.
    /// </summary>
    public void Correct(DateTimeOffset dueDate, string? customerName, string? customerEmail)
    {
        if (!IsDraft)
            throw new InvoiceStateException(
                $"Invoice {ProviderInvoiceId} is {Status.ToString().ToLowerInvariant()} and can no longer be corrected.");

        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Put the bill to the shopper. Afterwards a pay link can be handed out.</summary>
    public void MarkIssued(string? paymentLink)
    {
        if (IsWithdrawn)
            throw new InvoiceStateException($"Invoice {ProviderInvoiceId} has been withdrawn and can no longer be issued.");
        if (IsIssued)
            throw new InvoiceStateException($"Invoice {ProviderInvoiceId} has already been issued.");

        Status = InvoiceStatus.Issued;
        PaymentLink = paymentLink;
    }

    /// <summary>Take the bill back. Afterwards it is no longer payable and the pay link is not handed out.</summary>
    public void MarkWithdrawn()
    {
        if (IsWithdrawn)
            throw new InvoiceStateException($"Invoice {ProviderInvoiceId} has already been withdrawn.");

        Status = InvoiceStatus.Withdrawn;
        PaymentLink = null;
    }

    /// <summary>Refresh the cached pay link — only meaningful while the bill is issued.</summary>
    public void SetPaymentLink(string? paymentLink)
    {
        if (IsIssued)
            PaymentLink = paymentLink;
    }

    public void RecordProviderStatus(string? providerStatus) => ProviderStatus = providerStatus;
}
