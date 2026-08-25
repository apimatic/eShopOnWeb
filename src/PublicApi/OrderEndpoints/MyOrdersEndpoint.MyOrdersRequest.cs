namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; }

    public MyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}
