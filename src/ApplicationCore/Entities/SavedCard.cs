using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultToken, string? payPalCustomerId, string? cardBrand, string? last4, string? expiry)
    {
        BuyerId = buyerId;
        VaultToken = vaultToken;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string VaultToken { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
