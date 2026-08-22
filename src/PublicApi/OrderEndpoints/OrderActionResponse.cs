using System;
using System.Collections.Generic;

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
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderActionResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<int> NotificationIds { get; set; } = new();
}
