using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse() { }

    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
