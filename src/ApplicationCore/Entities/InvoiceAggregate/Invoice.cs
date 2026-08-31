using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the Visa/CyberSource
/// invoicing provider. eShop keeps enough of the provider-owned state — the identifier there and
/// where the bill currently stands — that a later request can act on it and report on it, since
/// the provider cannot call back into this application.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        string invoiceNumber,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string customerName,
        string customerEmail,
        string providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(invoiceNumber, nameof(invoiceNumber));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        InvoiceNumber = invoiceNumber;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderStatus = providerStatus;
        State = InvoiceLifecycleState.Raised;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper the bill belongs to (the order's buyer). Used to scope access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for the bill (used to act on and report on it later).</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The human-facing invoice number sent to the provider; also carries the eShop marker.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>What is billed — always derived from the order, never restated by a caller.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>eShop's authoritative lifecycle state for the bill.</summary>
    public InvoiceLifecycleState State { get; private set; }

    /// <summary>The last status string the provider reported, kept for reporting/reconciliation.</summary>
    public string ProviderStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsIssued => State == InvoiceLifecycleState.Issued;

    public bool IsWithdrawn => State == InvoiceLifecycleState.Withdrawn;

    /// <summary>Correcting is only possible while the bill has not been put to the shopper or withdrawn.</summary>
    public bool CanBeCorrected => State == InvoiceLifecycleState.Raised;

    /// <summary>The amount is not correctable here — only the due date and the customer details are.</summary>
    public void ApplyCorrection(DateOnly dueDate, string customerName, string customerEmail)
    {
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    public void MarkIssued() => State = InvoiceLifecycleState.Issued;

    public void MarkWithdrawn() => State = InvoiceLifecycleState.Withdrawn;

    public void SetProviderStatus(string providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }
}
