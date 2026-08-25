namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

// Request-side shapes shared by the order-payment and saved-card endpoints. Card details are
// only ever forwarded to PayPal (never persisted, never logged).
public class BillingAddressRequestDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;
}

public class CardDetailsRequestDto
{
    public string CardholderName { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string Cvv { get; set; } = string.Empty;
    public BillingAddressRequestDto BillingAddress { get; set; } = new();
}

public class AddressRequestDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
