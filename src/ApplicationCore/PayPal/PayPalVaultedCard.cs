namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalVaultedCard
{
    public PayPalVaultedCard(string paymentTokenId, string brand, string last4, string expiry)
    {
        PaymentTokenId = paymentTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public string PaymentTokenId { get; }
    public string Brand { get; }
    public string Last4 { get; }
    public string Expiry { get; }
}
