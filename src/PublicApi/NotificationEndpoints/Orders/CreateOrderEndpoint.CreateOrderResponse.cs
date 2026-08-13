using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Orders;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>The identifier of the order just placed.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>The notifications sent for placing the order, each with its own notificationId.</summary>
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
