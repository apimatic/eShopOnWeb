using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public List<OrderSummaryDto> Orders { get; set; } = new List<OrderSummaryDto>();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}