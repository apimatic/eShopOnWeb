namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    internal string UserId { get; set; } = "";
    public string Number { get; set; } = "";
    public string ExpiryYear { get; set; } = "";
    public string ExpiryMonth { get; set; } = "";
    public string Cvv { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "US";
}
