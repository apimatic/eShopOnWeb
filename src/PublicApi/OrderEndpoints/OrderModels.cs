using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST /api/orders. <see cref="BuyerId"/> is set server-side from the token.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Set from the caller's token; any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Response of POST /api/orders — carries the new order id as a top-level field.</summary>
public record PlaceOrderResponse(int OrderId, decimal Total, int ItemCount);

public record OrderActionResponse(int OrderId, string Status);

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationSummaryDto> Notifications);

public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);
