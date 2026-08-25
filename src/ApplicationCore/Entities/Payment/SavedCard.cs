using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultTokenId, string? lastFourDigits, string? cardBrand, string? expiry, string? cardType)
    {
        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        LastFourDigits = lastFourDigits;
        CardBrand = cardBrand;
        Expiry = expiry;
        CardType = cardType;
        CreatedAt = DateTimeOffset.UtcNow;
        IsDeleted = false;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
