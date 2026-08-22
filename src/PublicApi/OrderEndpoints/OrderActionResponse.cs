using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderActionResponse()
    {
    }

    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
}
