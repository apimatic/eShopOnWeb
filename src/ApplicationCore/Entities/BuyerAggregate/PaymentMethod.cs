namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }

    public PaymentMethod(int buyerId, string cardId, string brand, string last4, string expiry)
    {
        BuyerId = buyerId;
        CardId = cardId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public int BuyerId { get; private set; }
    public Buyer Buyer { get; private set; } = null!;
    public string CardId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string Last4 { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
}
