using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMyOrdersResponse()
    {
    }

    public List<BuyerOrderDto> Orders { get; set; } = new();
}

public class BuyerOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<BuyerOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class BuyerOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}
