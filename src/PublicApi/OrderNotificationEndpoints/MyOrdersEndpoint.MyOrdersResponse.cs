using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}
