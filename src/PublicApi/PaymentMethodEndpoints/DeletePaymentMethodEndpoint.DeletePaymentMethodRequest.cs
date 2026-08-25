namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(string buyerId, int paymentMethodId)
    {
        BuyerId = buyerId;
        PaymentMethodId = paymentMethodId;
    }

    public string BuyerId { get; }
    public int PaymentMethodId { get; }
}
