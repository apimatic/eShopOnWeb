using System;
namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }
    public PaymentMethod(string buyerId, string paypalTokenId, string brand, string lastFour, string expiry)
    { BuyerId = buyerId; PayPalTokenId = paypalTokenId; Brand = brand; LastFour = lastFour; Expiry = expiry; CreatedUtc = DateTimeOffset.UtcNow; }
    public string BuyerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string LastFour { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public DateTimeOffset CreatedUtc { get; private set; }
}
