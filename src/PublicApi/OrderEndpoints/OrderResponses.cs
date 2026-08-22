using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
