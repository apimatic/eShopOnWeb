using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record MyOrdersRequest
{
    public string BuyerId { get; init; } = "";
}

public record MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; init; } = new();
}

public record MyOrderDto
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = "";
    public decimal Total { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? Currency { get; init; }
    public decimal? TotalRefunded { get; init; }
    public List<OrderItemDto> Items { get; init; } = new();
}

public record OrderItemDto
{
    public string ProductName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}
