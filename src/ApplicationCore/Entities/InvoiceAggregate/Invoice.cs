using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A bill raised against an <see cref="OrderAggregate.Order"/> and held with the payment provider
/// (Visa / CyberSource). This is eShop's own record of a bill: it carries enough of the state the
/// provider owns — the provider's identifier for the bill and where it currently stands — that a
/// later request can act on it and report on it, not only the request that raised it.
///
/// The amount billed is a snapshot taken from the order at the moment the bill is raised; it is
/// never restated by a caller. Ownership is tracked via <see cref="BuyerId"/> so one shopper can
/// never see or correct another's bill.
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
        string currency,
        decimal amount,
        DateOnly dueDate,
        string? customerName,
        string? customerEmail,
        string status)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Currency = currency;
        Amount = amount;
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (and therefore the bill).</summary>
    public string BuyerId { get; private set; }

    /// <summary>
    /// The provider's identifier for the bill. Every subsequent provider call (fetch, issue,
    /// withdraw, correct) is keyed on this value. It is also the identifier eShop exposes to
    /// callers as <c>invoiceId</c>.
    /// </summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The three-letter currency the bill is denominated in (always USD for this account).</summary>
    public string Currency { get; private set; }

    /// <summary>The amount billed, snapshotted from the order. Not correctable.</summary>
    public decimal Amount { get; private set; }

    public DateOnly DueDate { get; private set; }

    public string? CustomerName { get; private set; }

    public string? CustomerEmail { get; private set; }

    /// <summary>Last known provider status. Refreshed whenever the provider is asked about the bill.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public bool IsDraft => InvoiceStatus.IsDraft(Status);

    public bool IsIssued => InvoiceStatus.IsIssued(Status);

    public bool IsWithdrawn => InvoiceStatus.IsWithdrawn(Status);

    /// <summary>
    /// Record the provider's latest status for this bill, tracking the moments it was put to the
    /// shopper and withdrawn so those transitions are not lost when the provider is re-queried.
    /// </summary>
    public void SyncStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (InvoiceStatus.IsIssued(status) && IssuedAt is null)
        {
            IssuedAt = DateTimeOffset.UtcNow;
        }
        if (InvoiceStatus.IsWithdrawn(status) && WithdrawnAt is null)
        {
            WithdrawnAt = DateTimeOffset.UtcNow;
        }

        Status = status;
    }

    /// <summary>
    /// Correct the due date and customer details the bill carries. Only valid while the bill is
    /// still a draft — the amount is never corrected here, as it comes from the order.
    /// </summary>
    public void ApplyCorrection(DateOnly dueDate, string? customerName, string? customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }
}
