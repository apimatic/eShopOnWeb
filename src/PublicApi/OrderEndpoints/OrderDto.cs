using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderRefundDto
{
    public string RefundId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderPaymentDto
{
    public string PayPalOrderId { get; set; } = "";
    public string AuthorizationId { get; set; } = "";
    public string AuthorizationStatus { get; set; } = "";
    public DateTimeOffset AuthorizationExpiresAt { get; set; }
    public int ReauthorizationCount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }
}
