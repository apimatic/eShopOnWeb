using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider.
/// eShop owns the mapping (which order, which shopper), the lifecycle stage it drives its own rules on
/// (<see cref="State"/>), and a cached snapshot of the provider-owned facts it needs to act on and report
/// the bill later — the provider identifier (<see cref="ProviderInvoiceId"/>), the last known provider
/// status string, and the customer-facing payment link once the bill has been issued. The authoritative
/// provider status/history is always re-read from the provider on a single-bill read.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(int orderId,
        string buyerId,
        string providerInvoiceId,
        string merchantReference,
        decimal amount,
        string currency,
        DateTimeOffset dueDate,
        string customerName,
        string customerEmail,
        string? status)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(merchantReference, nameof(merchantReference));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        MerchantReference = merchantReference;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderStatus = status;
        State = InvoiceState.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order — and therefore the bill. Only they may see or correct it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The bill's identifier at the provider. This is what callers pass on the invoice endpoints.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>
    /// The eShop-owned identifier stamped on the provider record (as merchantCustomerId) so a
    /// reconciliation scan can tell eShop's bills apart from bills raised by other activity on the
    /// shared provider account.
    /// </summary>
    public string MerchantReference { get; private set; }

    /// <summary>What is billed. Comes from the order and is never corrected on the bill.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>The eShop lifecycle stage this bill's own rules key on.</summary>
    public InvoiceState State { get; private set; }

    /// <summary>Last known provider status string (opaque, free-form). Refreshed on read/transition.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The customer-facing pay URL; null until the bill has been issued, cleared on withdrawal.</summary>
    public string? PaymentLink { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Whether this bill can still be corrected — only a draft can.</summary>
    public bool CanCorrect => State == InvoiceState.Draft;

    public void Correct(DateTimeOffset dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Refresh the cached provider-owned snapshot after reading it back from the provider.</summary>
    public void SyncProviderSnapshot(string? status, string? paymentLink)
    {
        ProviderStatus = status;
        // The provider mints a pay link as soon as the bill exists, but eShop only hands one out once the
        // bill has been put to the shopper — never for a draft or a withdrawn bill.
        PaymentLink = State == InvoiceState.Issued ? paymentLink : null;
    }

    public void MarkIssued(string? status, string? paymentLink)
    {
        State = InvoiceState.Issued;
        ProviderStatus = status;
        PaymentLink = paymentLink;
    }

    public void MarkWithdrawn(string? status)
    {
        State = InvoiceState.Withdrawn;
        ProviderStatus = status;
        PaymentLink = null;
    }
}
