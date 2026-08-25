using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() {}
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalCustomerId, string vaultId,
        string lastFour, string brand, string expiry)
    {
        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultId = vaultId;
        LastFour = lastFour;
        Brand = brand;
        Expiry = expiry;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string VaultId { get; private set; }
    public string LastFour { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SoftDelete() => IsDeleted = true;
}
