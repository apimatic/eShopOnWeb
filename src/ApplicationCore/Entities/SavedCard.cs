using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultTokenId, string lastFour, string brand, string expiry, string? cardholderName)
    {
        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        LastFour = lastFour;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string LastFour { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
