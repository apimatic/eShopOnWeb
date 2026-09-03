namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }
    public PaymentMethod(string ownerId, string providerToken, string brand, string last4, string expiry)
    {
        OwnerId = ownerId; CardId = providerToken; Alias = brand; Last4 = last4; Expiry = expiry;
    }
    public string OwnerId { get; private set; } = string.Empty;
    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // actual card data must be stored in a PCI compliant system, like Stripe
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
}
