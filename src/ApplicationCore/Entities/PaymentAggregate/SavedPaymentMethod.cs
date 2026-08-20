using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's vaulted card. Only PayPal's token and display metadata are kept — never the PAN or CVC.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalPaymentTokenId,
        string? payPalCustomerId,
        string brand,
        string last4,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);
}
