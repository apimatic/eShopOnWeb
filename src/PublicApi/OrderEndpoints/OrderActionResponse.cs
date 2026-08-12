using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The outcome of an operator order action (dispatch / cancel).</summary>
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

public class OrderActionRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }
}
