namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}
