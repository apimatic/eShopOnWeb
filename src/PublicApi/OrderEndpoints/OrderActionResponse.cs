namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse()
    {
    }

    public OrderActionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
