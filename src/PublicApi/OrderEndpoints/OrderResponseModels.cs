using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A line of an order in API responses.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order plus its payment state, shared by the order responses.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentCaptureId { get; set; }
    public string? PaymentRefundId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
