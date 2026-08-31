using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the invoicing provider.
/// The aggregate carries enough of the provider-owned state (<see cref="ProviderInvoiceId"/>,
/// <see cref="ProviderStatus"/>) that a later request can act on and report about the bill, not only
/// the one that raised it. What is billed (<see cref="Amount"/>) is sourced from the order and is never
/// corrected here.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        string merchantCustomerId,
        string description,
        decimal amount,
        string currency,
        DateTimeOffset dueDate,
        InvoiceCustomer customer,
        string? providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(merchantCustomerId, nameof(merchantCustomerId));
        Guard.Against.NullOrEmpty(description, nameof(description));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Null(customer, nameof(customer));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        MerchantCustomerId = merchantCustomerId;
        Description = description;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        Customer = customer;
        ProviderStatus = providerStatus;
        Status = InvoiceStatus.Draft;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (and therefore this bill). Non-operator callers see only their own.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this bill. Every later provider call is keyed on it.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>A stable eShop-scoped discriminator sent to the provider, used to tell eShop's bills apart during reconciliation.</summary>
    public string MerchantCustomerId { get; private set; }

    public string Description { get; private set; }

    /// <summary>What is billed. Sourced from the order; never corrected on the bill.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset DueDate { get; private set; }

    public InvoiceCustomer Customer { get; private set; }

    /// <summary>The provider's own last-known status string. Free-form and owned by the provider; may be null until fetched.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>eShop's authoritative lifecycle for this bill.</summary>
    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsDraft => Status == InvoiceStatus.Draft;
    public bool IsIssued => Status == InvoiceStatus.Issued;
    public bool IsWithdrawn => Status == InvoiceStatus.Withdrawn;

    /// <summary>Whether a payment link may be handed out for this bill (issued and not withdrawn).</summary>
    public bool IsPayable => Status == InvoiceStatus.Issued;

    /// <summary>
    /// Correct the due date and/or customer details. Permitted only while the bill is still a draft;
    /// once it has been put to the shopper or withdrawn, correcting it throws rather than silently doing nothing.
    /// The amount is not correctable here — it comes from the order.
    /// </summary>
    public void ApplyCorrection(DateTimeOffset? dueDate, string? customerName, string? customerEmail)
    {
        if (!IsDraft)
        {
            throw new InvoiceNotCorrectableException(Id, Status);
        }

        if (dueDate.HasValue)
        {
            DueDate = dueDate.Value;
        }

        if (customerName is not null || customerEmail is not null)
        {
            Customer = new InvoiceCustomer(
                string.IsNullOrWhiteSpace(customerName) ? Customer.Name : customerName!,
                string.IsNullOrWhiteSpace(customerEmail) ? Customer.Email : customerEmail!);
        }
    }

    /// <summary>Record that the bill has been put to the shopper. Refused on a withdrawn bill.</summary>
    public void MarkIssued(string? providerStatus)
    {
        if (IsWithdrawn)
        {
            throw new InvoiceTransitionException(Id, Status, "issue");
        }

        Status = InvoiceStatus.Issued;
        SyncProviderStatus(providerStatus);
    }

    /// <summary>Record that the bill has been withdrawn. After this it is no longer payable.</summary>
    public void MarkWithdrawn(string? providerStatus)
    {
        Status = InvoiceStatus.Withdrawn;
        SyncProviderStatus(providerStatus);
    }

    /// <summary>Refresh the provider's last-known status string from a provider response.</summary>
    public void SyncProviderStatus(string? providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }
}
