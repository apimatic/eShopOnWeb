using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : AuthenticatedRequest
{
    /// <summary>Catalog item ids and quantities to order.</summary>
    public List<OrderLineDto> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the created order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OrderActionRequest : AuthenticatedRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderStateResponse : BaseResponse
{
    public OrderStateResponse(Guid correlationId) : base(correlationId) { }
    public OrderStateResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrdersRequest : AuthenticatedRequest
{
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }

    /// <summary>Each notification about this order and where it got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderNotificationsRequest : AuthenticatedRequest
{
    public OrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
