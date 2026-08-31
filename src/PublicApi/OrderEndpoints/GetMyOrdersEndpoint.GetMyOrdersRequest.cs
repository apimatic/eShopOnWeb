using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
}

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public GetMyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new List<OrderDto>();
}
