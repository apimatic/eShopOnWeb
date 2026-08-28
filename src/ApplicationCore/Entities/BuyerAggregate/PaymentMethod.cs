namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentMethod() { }

    public PaymentMethod(string alias, string payPalVaultId, string brand, string last4, string expiry)
    {
        Alias = alias;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public string Alias { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public System.DateTimeOffset CreatedAt { get; private set; } = System.DateTimeOffset.UtcNow;
}
