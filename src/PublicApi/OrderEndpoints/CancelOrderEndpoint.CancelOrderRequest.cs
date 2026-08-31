using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : CancellableRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; init; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderStatus Status { get; set; }
}
