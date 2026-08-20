using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
}

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMyOrdersResponse()
    {
    }

    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<ShopperOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ShopperOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
