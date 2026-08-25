namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string SecurityCode { get; set; } = "";
    public string? Name { get; set; }
}
