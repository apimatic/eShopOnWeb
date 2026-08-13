using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Orders;

/// <summary>The result of an operator order action (dispatch / cancel), with the order's notifications.</summary>
public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderActionResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>The order's notifications, each with its own notificationId for further operator actions.</summary>
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
