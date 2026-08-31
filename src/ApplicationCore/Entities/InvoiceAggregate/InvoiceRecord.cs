using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop's own durable record of a bill raised against an <see cref="OrderAggregate.Order"/> with
/// the payment provider. The provider (Visa/CyberSource) owns the authoritative state; this entity
/// carries enough of it — the provider's identifier and last-known status — that a later request can
/// act on and report about the bill, and so that eShop can reconcile its records against the provider.
/// </summary>
public class InvoiceRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private InvoiceRecord() { }
#pragma warning restore CS8618

    public InvoiceRecord(
        int orderId,
        string buyerId,
        string providerInvoiceId,
        string status,
        int itemCount,
        decimal amount,
        string currency,
        string customerName,
        string customerEmail,
        DateTime dueDate,
        string description)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(providerInvoiceId, nameof(providerInvoiceId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        ProviderInvoiceId = providerInvoiceId;
        Status = status;
        ItemCount = itemCount;
        Amount = amount;
        Currency = currency;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        DueDate = dueDate;
        Description = description;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The eShop order this bill was raised against.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order — and therefore the bill.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's identifier for this bill (the handle every later call uses).</summary>
    public string ProviderInvoiceId { get; private set; }

    /// <summary>The provider's last-known status for this bill.</summary>
    public string Status { get; private set; }

    public int ItemCount { get; private set; }

    /// <summary>What is billed — sourced from the order, never restated by the caller.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    public DateTime DueDate { get; private set; }

    public string Description { get; private set; }

    /// <summary>The last payment link the provider handed out, if the bill is payable.</summary>
    public string? PaymentLink { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsPutToShopper => InvoiceStatus.IsPutToShopper(Status);

    public bool IsWithdrawn => InvoiceStatus.IsWithdrawn(Status);

    public bool CanBeCorrected => InvoiceStatus.CanBeCorrected(Status);

    /// <summary>
    /// Synchronise the last-known provider state onto this record after asking the provider.
    /// A payment link is only retained while the bill is genuinely payable.
    /// </summary>
    public void SyncFromProvider(string status, string? paymentLink)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        PaymentLink = InvoiceStatus.IsWithdrawn(status) ? null : paymentLink;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Apply a correction to the customer details and/or due date this bill carries. The billed
    /// amount is intentionally not correctable here — it always comes from the order.
    /// </summary>
    public void ApplyCorrection(DateTime dueDate, string customerName, string customerEmail)
    {
        DueDate = dueDate;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
