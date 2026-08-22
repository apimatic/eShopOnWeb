using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ShopOrderEndpoints;

public class ShopOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<ShopOrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ShopOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<ShopOrderDto> Orders { get; set; } = new();
}
