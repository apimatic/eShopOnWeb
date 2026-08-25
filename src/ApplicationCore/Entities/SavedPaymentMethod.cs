using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalCustomerId, string vaultTokenId,
        string lastDigits, string brand, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultTokenId = vaultTokenId;
        LastDigits = lastDigits;
        Brand = brand;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string? Expiry { get; private set; }
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
