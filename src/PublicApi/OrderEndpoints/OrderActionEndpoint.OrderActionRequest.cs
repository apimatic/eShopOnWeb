using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionRequest : BaseRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderActionResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
