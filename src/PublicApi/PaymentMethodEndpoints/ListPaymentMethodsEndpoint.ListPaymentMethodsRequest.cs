namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsRequest : BaseRequest
{
    public ListPaymentMethodsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}
