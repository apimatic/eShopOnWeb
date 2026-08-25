namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public VaultCardDetails? Card { get; set; }
}

public class VaultCardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? CountryCode { get; set; }
    public string? PostalCode { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? LastFourDigits { get; set; }
    public string? CardBrand { get; set; }
    public string? Expiry { get; set; }
    public string? CardType { get; set; }
}
