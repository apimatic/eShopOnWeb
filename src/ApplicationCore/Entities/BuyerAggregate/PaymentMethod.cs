namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }
    public PaymentMethod(string token, string brand, string last4, string expiry, string? name)
    { PayPalTokenId = token; Brand = brand; Last4 = last4; Expiry = expiry; CardholderName = name; }
    public int BuyerId { get; private set; }
    public string PayPalTokenId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public string? CardholderName { get; private set; }
    public System.DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => RemovedAt == null;
    public void Remove(System.DateTimeOffset at) => RemovedAt = at;
}
