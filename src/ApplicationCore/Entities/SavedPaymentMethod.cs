using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string userId, string vaultToken,
        string? last4Digits, string? cardBrand, string? expiry)
    {
        UserId = userId;
        VaultToken = vaultToken;
        Last4Digits = last4Digits;
        CardBrand = cardBrand;
        Expiry = expiry;
    }

    public string UserId { get; private set; }
    public string VaultToken { get; private set; }
    public string? Last4Digits { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Expiry { get; private set; }
}
