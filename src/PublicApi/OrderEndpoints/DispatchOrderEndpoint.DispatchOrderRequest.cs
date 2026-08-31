using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : CancellableRequest
{
    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; init; }
}

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId) { }
    public DispatchOrderResponse() { }

    public int OrderId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderStatus Status { get; set; }
}
