using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's saved card. Full PAN/CVC are never stored — only PayPal's vault token
/// and display-safe descriptors.
/// </summary>
public class ShopperPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ShopperPaymentMethod() { }
#pragma warning restore CS8618

    public ShopperPaymentMethod(
        string buyerId,
        string payPalPaymentTokenId,
        string? payPalCustomerId,
        string? brand,
        string? last4,
        string? expiry,
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
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
