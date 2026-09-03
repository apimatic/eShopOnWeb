namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, Microsoft.eShopWeb.ApplicationCore.Interfaces.IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string ownerId, string providerTokenId, string brand, string last4,
        string expiry, System.DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(providerTokenId) ||
            string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(last4) || string.IsNullOrWhiteSpace(expiry))
        {
            throw new System.ArgumentException("Payment method fields are required.");
        }

        OwnerId = ownerId;
        ProviderTokenId = providerTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; }
    public string ProviderTokenId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public System.DateTimeOffset CreatedAt { get; private set; }
}
