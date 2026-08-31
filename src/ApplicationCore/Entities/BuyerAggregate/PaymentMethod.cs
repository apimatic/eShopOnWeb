namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, Microsoft.eShopWeb.ApplicationCore.Interfaces.IAggregateRoot
{
    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault token; never card data
    public string? Last4 { get; private set; }
    public string OwnerId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public bool IsDeleted { get; private set; }

    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string cardId, string last4, string brand, string expiry,
        string? paypalCustomerId, string? alias)
    {
        OwnerId = ownerId;
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        PayPalCustomerId = paypalCustomerId;
        Alias = alias;
    }

    public void Delete() => IsDeleted = true;
}
