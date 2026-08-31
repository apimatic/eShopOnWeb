using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the invoicing provider.
/// The amount and line items are a snapshot of the order at the moment the bill was raised; the amount
/// is never corrected here because it comes from the order. The record carries enough of the state the
/// provider owns (its identifier there and the last status it reported) that a later request can act on
/// and report the bill, not only the request that raised it.
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
        decimal amount,
        string currency,
        DateTimeOffset dueDate,
        string customerName,
        string customerEmail,
        string? providerStatus)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(invoiceNumber, nameof(invoiceNumber));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

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
        State = InvoiceState.Draft;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper the bill belongs to (the order's buyer). Used to scope access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's own identifier for this invoice. This is the public invoice id.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The human-readable invoice number we assigned; also identifies this app's invoices.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The billed amount, snapshotted from the order. Not correctable.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset DueDate { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    /// <summary>The last status string the provider reported for this invoice, if any.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>This application's view of where the bill has got to.</summary>
    public InvoiceState State { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public bool IsDraft => State == InvoiceState.Draft;
    public bool IsIssued => State == InvoiceState.Issued;
    public bool IsWithdrawn => State == InvoiceState.Withdrawn;

    /// <summary>
    /// Correct the due date and customer details while the bill is still a draft. The amount is not
    /// touched because it comes from the order.
    /// </summary>
    public void ApplyCorrection(DateTimeOffset dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    public void MarkIssued(string? providerStatus)
    {
        State = InvoiceState.Issued;
        SyncProviderStatus(providerStatus);
    }

    public void MarkWithdrawn(string? providerStatus)
    {
        State = InvoiceState.Withdrawn;
        SyncProviderStatus(providerStatus);
    }

    /// <summary>Record the latest status string the provider reported, without changing our lifecycle.</summary>
    public void SyncProviderStatus(string? providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
    }
}
