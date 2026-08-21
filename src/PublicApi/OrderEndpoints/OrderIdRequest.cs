namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderActionResponse : BaseResponse
{
    public OrderDto Order { get; set; } = new();
}
