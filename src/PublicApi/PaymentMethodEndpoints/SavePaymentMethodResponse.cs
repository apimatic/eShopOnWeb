namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? CardBrand { get; set; }
}
