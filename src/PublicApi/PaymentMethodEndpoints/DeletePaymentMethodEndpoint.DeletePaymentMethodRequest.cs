namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
