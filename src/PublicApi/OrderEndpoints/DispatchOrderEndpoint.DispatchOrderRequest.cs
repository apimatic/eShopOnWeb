using System;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId) {}
    public DispatchOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
