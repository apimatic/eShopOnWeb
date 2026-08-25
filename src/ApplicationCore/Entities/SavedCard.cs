using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? last4, string? cardBrand, string? expiry)
    {
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Last4 = last4;
        CardBrand = cardBrand;
        Expiry = expiry;
        SavedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Last4 { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset SavedAt { get; private set; }
}
