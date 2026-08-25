using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultTokenId, string? last4, string? brand, string? expiry, string? paypalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        PayPalCustomerId = paypalCustomerId;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? PayPalCustomerId { get; private set; }
}
