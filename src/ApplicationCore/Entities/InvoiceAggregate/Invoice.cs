using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider.
/// This aggregate owns eShop's record of the bill: who it belongs to, which order it was raised
/// against, and enough of the provider-owned state (its identifier there and where it locally stands)
/// that a later request can act on it and report on it. The money that is billed always comes from
/// the order, so it is captured here only as a snapshot for reporting and is never corrected directly.
/// </summary>
public class Invoice : BaseEntity, IAggregateRoot
{
    /// <summary>The order this bill was raised against. What is billed always comes from this order.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this bill (the buyer of the order). Only they — or an operator — may see or correct it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The human-facing invoice number carried to the provider. Prefixed so eShop bills are distinguishable from other bills on the shared provider account.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The identifier the provider assigned to this bill. Empty until the bill has actually been raised with the provider.</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>Snapshot of the amount billed, taken from the order at the time the bill was raised. Not correctable here.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The currency the bill is raised in. This account bills in USD.</summary>
    public string Currency { get; private set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The customer name the bill carries.</summary>
    public string CustomerName { get; private set; }

    /// <summary>The customer email the bill carries.</summary>
    public string CustomerEmail { get; private set; }

    /// <summary>Where the bill locally stands in its lifecycle.</summary>
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    /// <summary>When eShop raised the bill (UTC).</summary>
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Invoice() { }
#pragma warning restore CS8618

    public Invoice(int orderId, string buyerId, string invoiceNumber, decimal amount, string currency,
        DateOnly dueDate, string customerName, string customerEmail)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(invoiceNumber, nameof(invoiceNumber));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        OrderId = orderId;
        BuyerId = buyerId;
        InvoiceNumber = invoiceNumber;
        Amount = amount;
        Currency = currency;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        ProviderInvoiceId = string.Empty;
    }

    /// <summary>Records the identifier the provider assigned once the bill has been raised there.</summary>
    public void SetProviderInvoiceId(string providerInvoiceId)
    {
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        ProviderInvoiceId = providerInvoiceId;
    }

    /// <summary>
    /// Corrects the due date and the customer details the bill carries. The amount is not correctable —
    /// it comes from the order. Only possible while the bill has not yet been put to the shopper and has
    /// not been withdrawn; otherwise the caller is told rather than the change silently doing nothing.
    /// </summary>
    public void CorrectDetails(DateOnly dueDate, string customerName, string customerEmail)
    {
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));
        EnsureCorrectable();
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>Marks the bill as put to the shopper. A withdrawn bill can never be issued.</summary>
    public void MarkIssued()
    {
        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {InvoiceNumber} has been withdrawn and can no longer be issued to the customer.");
        }

        Status = InvoiceStatus.Issued;
    }

    /// <summary>Marks the bill as withdrawn. After this it must no longer be payable.</summary>
    public void MarkWithdrawn()
    {
        Status = InvoiceStatus.Withdrawn;
    }

    private void EnsureCorrectable()
    {
        if (Status == InvoiceStatus.Issued)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {InvoiceNumber} has already been issued to the customer and can no longer be corrected.");
        }

        if (Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {InvoiceNumber} has been withdrawn and can no longer be corrected.");
        }
    }
}
