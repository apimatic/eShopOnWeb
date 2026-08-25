namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDetails? Card { get; set; }
}

public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
    public string PostalCode { get; set; } = string.Empty;
}
