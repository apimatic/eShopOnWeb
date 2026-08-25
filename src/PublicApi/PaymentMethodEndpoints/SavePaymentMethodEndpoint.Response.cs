namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Last4Digits { get; set; }
    public string? CardBrand { get; set; }
    public string? Expiry { get; set; }
}
