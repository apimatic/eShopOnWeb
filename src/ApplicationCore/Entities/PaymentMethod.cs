using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }
    public PaymentMethod(string ownerId, string paypalTokenId, string brand, string lastFour, int expiryMonth, int expiryYear)
    { OwnerId = ownerId; PayPalTokenId = paypalTokenId; Brand = brand; LastFour = lastFour; ExpiryMonth = expiryMonth; ExpiryYear = expiryYear; }
    public string OwnerId { get; private set; } = string.Empty;
    public string PayPalTokenId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string LastFour { get; private set; } = string.Empty;
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }
}
