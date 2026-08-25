namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public string CardNumber { get; set; } = "";
    public string CardExpiry { get; set; } = ""; // YYYY-MM
    public string CardCvv { get; set; } = "";
    public string? CardholderName { get; set; }
}
