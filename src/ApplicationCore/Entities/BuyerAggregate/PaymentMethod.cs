namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }
    public PaymentMethod(string ownerId, string paypalTokenId, string? last4, string? brand)
    { OwnerId = ownerId; PayPalTokenId = paypalTokenId; Last4 = last4; Brand = brand; }
    public string OwnerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string? Alias { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
}
