using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the Visa/CyberSource
/// invoicing provider. eShopOnWeb persists just enough of the provider-owned state — the provider's
/// invoice identifier and where the bill currently stands — for a later request to act on and report
/// on the bill, not only the one that raised it.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
    #pragma warning restore CS8618

    public Invoice(int orderId, string buyerId, string providerInvoiceId, decimal amount,
        string currency, DateTimeOffset dueDate, string customerName, string customerEmail)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Status = InvoiceStatus.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShopOnWeb order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (the buyer id of the order). Used for access control.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's invoice identifier — the public invoice id this application acts on.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The amount billed, taken from the order, never restated by a caller.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateTimeOffset DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>The link a shopper uses to pay, once the bill has been put to them.</summary>
    public string? PaymentLink { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public bool IsDraft => Status == InvoiceStatus.Draft;
    public bool IsIssued => Status == InvoiceStatus.Issued;
    public bool IsWithdrawn => Status == InvoiceStatus.Withdrawn;

    /// <summary>
    /// Correct the due date and customer details while the bill is still a draft. The amount is not
    /// correctable here — it comes from the order.
    /// </summary>
    public void Amend(DateTimeOffset dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Record that the bill has been put to the shopper and capture how they can pay it.</summary>
    public void MarkIssued(string? paymentLink)
    {
        Status = InvoiceStatus.Issued;
        PaymentLink = paymentLink;
    }

    /// <summary>Record that the bill has been withdrawn; the way to pay it is no longer handed out.</summary>
    public void MarkWithdrawn()
    {
        Status = InvoiceStatus.Withdrawn;
        PaymentLink = null;
    }
}
