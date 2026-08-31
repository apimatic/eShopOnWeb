using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the invoicing provider.
/// The entity carries enough of the state the provider owns — its identifier there and where it
/// currently stands — that a later request can act on and report on the bill, not only the one that
/// raised it. What is billed always derives from the order, so no amount is stored as a correctable field.
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
        string? providerStatus,
        DateTimeOffset dueDate,
        decimal totalAmount,
        string currency,
        string customerName,
        string? customerEmail,
        string merchantCustomerId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(merchantCustomerId, nameof(merchantCustomerId));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        ProviderStatus = providerStatus;
        DueDate = dueDate;
        TotalAmount = totalAmount;
        Currency = currency;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        MerchantCustomerId = merchantCustomerId;
        State = InvoiceState.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (their eShop username). One shopper never sees another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The bill's identifier at the provider — the key every later provider call is made with.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The last status string the provider reported for this bill, stored verbatim.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The application-authoritative lifecycle state.</summary>
    public InvoiceState State { get; private set; }

    /// <summary>The customer-facing way to pay, once the bill has been put to the shopper.</summary>
    public string? PaymentLink { get; private set; }

    public DateTimeOffset DueDate { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; }
    public string CustomerName { get; private set; }
    public string? CustomerEmail { get; private set; }

    /// <summary>An application-controlled tag echoed back by the provider, used to line up its record with eShop's.</summary>
    public string MerchantCustomerId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>True only once the bill has been put to the shopper and not since withdrawn.</summary>
    public bool IsPayable => State == InvoiceState.Issued;

    /// <summary>The pay link, but only handed out while the bill is genuinely payable.</summary>
    public string? PayableLink => IsPayable ? PaymentLink : null;

    /// <summary>Correct the due date and customer details of a bill that is still a draft.</summary>
    public void ApplyDraftCorrection(DateTimeOffset dueDate, string customerName, string? customerEmail)
    {
        if (State != InvoiceState.Draft)
            throw new InvoiceNotModifiableException(ProviderInvoiceId, State, "corrected");

        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Put the bill to the shopper. Only a draft can be issued.</summary>
    public void MarkIssued(string? providerStatus, string? paymentLink)
    {
        if (State != InvoiceState.Draft)
            throw new InvoiceNotModifiableException(ProviderInvoiceId, State, "issued");

        State = InvoiceState.Issued;
        ProviderStatus = providerStatus;
        PaymentLink = paymentLink;
    }

    /// <summary>Withdraw the bill so it is no longer payable. A bill can only be withdrawn once.</summary>
    public void MarkWithdrawn(string? providerStatus)
    {
        if (State == InvoiceState.Withdrawn)
            throw new InvoiceNotModifiableException(ProviderInvoiceId, State, "withdrawn again");

        State = InvoiceState.Withdrawn;
        ProviderStatus = providerStatus;
        PaymentLink = null;
    }

    /// <summary>
    /// Refresh the provider-owned facts (status, and — while issued — the pay link) from a fresh read,
    /// without changing the application lifecycle state.
    /// </summary>
    public void SyncProviderState(string? providerStatus, string? paymentLink)
    {
        ProviderStatus = providerStatus;
        if (State == InvoiceState.Issued && !string.IsNullOrWhiteSpace(paymentLink))
            PaymentLink = paymentLink;
    }
}
