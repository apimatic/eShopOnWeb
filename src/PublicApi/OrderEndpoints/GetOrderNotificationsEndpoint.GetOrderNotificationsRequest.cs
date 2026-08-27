using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }

    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsOperator { get; set; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public GetOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public GetOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}
