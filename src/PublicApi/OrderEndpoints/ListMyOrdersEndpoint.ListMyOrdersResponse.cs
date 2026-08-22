using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool BodyRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMyOrdersResponse()
    {
    }

    public List<MyOrderDto> Orders { get; set; } = new();
}
