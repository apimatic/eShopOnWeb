using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public int? ResendOfNotificationId { get; set; }
}
