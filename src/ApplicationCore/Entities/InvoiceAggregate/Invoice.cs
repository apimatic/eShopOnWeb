using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider
/// (Visa/CyberSource). eShop owns the lifecycle (<see cref="Status"/>) and the link back to the order
/// and its owner; the provider owns the money movement. Enough of the provider's state — its identifier
/// there (<see cref="ProviderInvoiceId"/>) and where it currently stands (<see cref="ProviderStatus"/>,
/// <see cref="PaymentLink"/>) — is persisted here that a later request can act on and report about the
/// bill without having raised it.
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
        string description,
        decimal amount,
        string currencyCode,
        DateTimeOffset dueDate,
        string? customerName,
        string? customerEmail,
        string? providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(description, nameof(description));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Description = description;
        Amount = amount;
        CurrencyCode = currencyCode;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderStatus = providerStatus;
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper (the order's buyer). Used to scope one shopper away from another's bills.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this bill — the handle every later provider call is made against.</summary>
    public string ProviderInvoiceId { get; private set; }

    public string Description { get; private set; }

    /// <summary>What is billed, taken from the order. Never restated by a caller.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public DateTimeOffset DueDate { get; private set; }

    public string? CustomerName { get; private set; }

    public string? CustomerEmail { get; private set; }

    /// <summary>eShop's authoritative lifecycle state.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>The provider's own last-reported status string (advisory; the vocabulary is the provider's).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>How the shopper can pay, once the bill has been put to them. Cleared on withdrawal.</summary>
    public string? PaymentLink { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True while the bill may still be corrected (not yet put to the shopper, not withdrawn).</summary>
    public bool IsCorrectable => Status == InvoiceStatus.Draft;

    /// <summary>Record the provider's latest reported status without changing eShop's lifecycle state.</summary>
    public void RecordProviderStatus(string? providerStatus)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }

    /// <summary>
    /// Correct the due date and customer details of a still-draft bill. The billed amount is not
    /// correctable — it stays what the order says.
    /// </summary>
    public void Revise(DateTimeOffset dueDate, string? customerName, string? customerEmail)
    {
        if (!IsCorrectable)
        {
            throw new InvalidInvoiceStateException(
                Id,
                $"This bill can no longer be corrected because it has been {Describe(Status)}.");
        }

        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Put the bill to the shopper. Only a draft can be issued.</summary>
    public void Issue(string? paymentLink, string? providerStatus)
    {
        if (Status == InvoiceStatus.Issued)
        {
            throw new InvalidInvoiceStateException(Id, "This bill has already been put to the shopper.");
        }
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceStateException(Id, "This bill has been withdrawn and cannot be put to the shopper.");
        }

        Status = InvoiceStatus.Issued;
        PaymentLink = paymentLink;
        RecordProviderStatus(providerStatus);
    }

    /// <summary>Withdraw the bill. Afterwards it is no longer payable and the payment link is withheld.</summary>
    public void Withdraw(string? providerStatus)
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceStateException(Id, "This bill has already been withdrawn.");
        }

        Status = InvoiceStatus.Withdrawn;
        PaymentLink = null;
        RecordProviderStatus(providerStatus);
    }

    private static string Describe(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Issued => "put to the shopper",
        InvoiceStatus.Withdrawn => "withdrawn",
        _ => "created"
    };
}
