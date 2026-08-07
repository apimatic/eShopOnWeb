namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodResponse : BaseResponse
{
    public string Status { get; set; } = "deleted";
}
