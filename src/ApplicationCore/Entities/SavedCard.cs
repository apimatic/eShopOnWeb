using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string shopperId, string payPalPaymentTokenId, string payPalCustomerId,
        string merchantCustomerId, string? lastFourDigits, string? cardBrand,
        string? cardExpiry, string? cardHolderName)
    {
        ShopperId = shopperId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        MerchantCustomerId = merchantCustomerId;
        LastFourDigits = lastFourDigits;
        CardBrand = cardBrand;
        CardExpiry = cardExpiry;
        CardHolderName = cardHolderName;
        CreatedAt = DateTime.UtcNow;
    }

    public string ShopperId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string MerchantCustomerId { get; private set; } = string.Empty;
    public string? LastFourDigits { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardExpiry { get; private set; }
    public string? CardHolderName { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
