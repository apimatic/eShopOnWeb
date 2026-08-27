using System;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) {}
    public CancelOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
