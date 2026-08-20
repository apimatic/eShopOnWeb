using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string paypalPaymentTokenId,
        string? paypalCustomerId,
        string lastDigits,
        string brand,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paypalPaymentTokenId, nameof(paypalPaymentTokenId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        PayPalPaymentTokenId = paypalPaymentTokenId;
        PayPalCustomerId = paypalCustomerId;
        LastDigits = lastDigits;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
