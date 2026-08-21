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
        string lastDigits,
        string brand,
        string expiry,
        string? cardholderName,
        string? paypalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paypalPaymentTokenId, nameof(paypalPaymentTokenId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));

        BuyerId = buyerId;
        PayPalPaymentTokenId = paypalPaymentTokenId;
        LastDigits = lastDigits;
        Brand = string.IsNullOrWhiteSpace(brand) ? "CARD" : brand;
        Expiry = expiry ?? string.Empty;
        CardholderName = cardholderName;
        PayPalCustomerId = paypalCustomerId;
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
