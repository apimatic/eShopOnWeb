namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string vaultId, string last4, string? brand, string? expiry, string? cardholderName)
    {
        CardId = vaultId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        Alias = BuildAlias(brand, last4);
    }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault token id — never a PAN
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    private static string BuildAlias(string? brand, string? last4)
    {
        var network = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return string.IsNullOrWhiteSpace(last4) ? network : $"{network} ending {last4}";
    }
}
