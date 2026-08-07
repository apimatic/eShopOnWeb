using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}
