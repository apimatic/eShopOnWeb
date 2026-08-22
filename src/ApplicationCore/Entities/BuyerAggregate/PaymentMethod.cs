namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault payment-token id — never a PAN
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }

    #pragma warning disable CS8618
    private PaymentMethod() { }
    #pragma warning restore CS8618

    public PaymentMethod(string cardId, string? last4, string? brand, string? expiry, string? alias)
    {
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        Alias = alias;
    }

    public void UpdateDisplay(string? last4, string? brand, string? expiry, string? alias)
    {
        Last4 = last4 ?? Last4;
        Brand = brand ?? Brand;
        Expiry = expiry ?? Expiry;
        Alias = alias ?? Alias;
    }
}
