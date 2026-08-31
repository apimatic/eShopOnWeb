namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, Microsoft.eShopWeb.ApplicationCore.Interfaces.IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string paypalTokenId, string? paypalCustomerId,
        string brand, string last4, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = paypalTokenId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = System.DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalTokenId { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; private set; }
    public System.DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;
    public void MarkDeleted() => DeletedAt ??= System.DateTimeOffset.UtcNow;
}
