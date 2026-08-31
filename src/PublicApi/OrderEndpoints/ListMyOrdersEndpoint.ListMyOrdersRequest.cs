using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersRequest : CancellableRequest
{
    public string? BuyerId { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}
