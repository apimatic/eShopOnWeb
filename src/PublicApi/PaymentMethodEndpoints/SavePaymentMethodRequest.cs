namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public int CardExpiryMonth { get; set; }
    public int CardExpiryYear { get; set; }
    public string? CardCvv { get; set; }
    public string? CardholderName { get; set; }
    public string BillingCountryCode { get; set; } = string.Empty;
    public string? BillingPostalCode { get; set; }
}
