namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
}
