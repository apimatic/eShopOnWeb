using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShopOnWeb's own record of a bill that was raised against an <see cref="OrderAggregate.Order"/>
/// with the invoicing provider. It carries enough of the provider-owned state (the identifier there
/// and where the bill currently stands) that a later request can act on and report about the bill,
/// not only the request that raised it.
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
        decimal totalAmount,
        string currency,
        DateOnly dueDate,
        string customerName,
        string customerEmail,
        DateTimeOffset createdDate,
        string providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(invoiceNumber, nameof(invoiceNumber));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        InvoiceNumber = invoiceNumber;
        TotalAmount = totalAmount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        CreatedDate = createdDate;
        ProviderStatus = providerStatus;
        LifecycleState = InvoiceLifecycleState.Draft;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (and therefore the bill). Used to scope access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The identifier of the bill in the provider's system. This is the public invoiceId.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The human-facing invoice number sent to the provider (equals the provider id).</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>What is billed, taken from the order. Not correctable.</summary>
    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>The way to pay the bill. Only handed out once it has been put to the shopper.</summary>
    public string? PaymentLink { get; private set; }

    /// <summary>When the provider recorded the bill as raised.</summary>
    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>eShop's lifecycle stage; governs which caller actions are still allowed.</summary>
    public InvoiceLifecycleState LifecycleState { get; private set; }

    /// <summary>Last known status reported by the provider (DRAFT/SENT/PARTIAL/PAID/CANCELED/...).</summary>
    public string ProviderStatus { get; private set; }

    /// <summary>A bill can only be corrected while it has not yet been put to the shopper.</summary>
    public bool CanBeCorrected => LifecycleState == InvoiceLifecycleState.Draft;

    /// <summary>Correct the due date and the customer details the bill carries. The amount is not correctable.</summary>
    public void ApplyCorrections(DateOnly dueDate, string customerName, string customerEmail)
    {
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Record that the bill has been put to the shopper, capturing the way to pay it.</summary>
    public void MarkIssued(string? paymentLink, string providerStatus)
    {
        LifecycleState = InvoiceLifecycleState.Issued;
        PaymentLink = paymentLink;
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }

    /// <summary>Record that the bill has been withdrawn; it is no longer payable.</summary>
    public void MarkWithdrawn(string providerStatus)
    {
        LifecycleState = InvoiceLifecycleState.Withdrawn;
        PaymentLink = null;
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }

    /// <summary>Refresh the last-known provider state from a live read, without changing the local lifecycle.</summary>
    public void SyncProviderState(string providerStatus, string? paymentLink)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }

        // A withdrawn bill must never surface a payment link again.
        PaymentLink = LifecycleState == InvoiceLifecycleState.Withdrawn ? null : paymentLink;
    }
}
