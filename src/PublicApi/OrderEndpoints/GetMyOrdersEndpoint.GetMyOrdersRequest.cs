namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}
