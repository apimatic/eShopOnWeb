namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
}
