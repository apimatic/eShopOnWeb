namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, Microsoft.eShopWeb.ApplicationCore.Interfaces.IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string paypalTokenId, string paypalCustomerId, string brand, string last4, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = paypalTokenId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string PayPalCustomerId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string Last4 { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public System.DateTimeOffset CreatedAt { get; private set; } = System.DateTimeOffset.UtcNow;
}
