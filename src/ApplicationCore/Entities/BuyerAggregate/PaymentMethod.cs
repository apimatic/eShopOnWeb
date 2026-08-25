namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    public int BuyerId { get; private set; }
    public string? Alias { get; private set; }
    public string? VaultToken { get; private set; }
    public string? Last4 { get; private set; }
    public string? CardBrand { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }

#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(int buyerId, string vaultToken, string? last4, string? cardBrand,
        string? expiryMonth, string? expiryYear, string? alias = null)
    {
        BuyerId = buyerId;
        VaultToken = vaultToken;
        Last4 = last4;
        CardBrand = cardBrand;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Alias = alias;
    }
}
