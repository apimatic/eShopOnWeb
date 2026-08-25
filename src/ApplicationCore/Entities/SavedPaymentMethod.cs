using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerEmail, string payPalCustomerId, string payPalVaultId,
        string last4, string brand, string expiry)
    {
        BuyerEmail = buyerEmail;
        PayPalCustomerId = payPalCustomerId;
        PayPalVaultId = payPalVaultId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
    }

    public string BuyerEmail { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string Last4 { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
}
