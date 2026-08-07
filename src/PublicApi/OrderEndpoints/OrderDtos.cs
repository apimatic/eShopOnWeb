using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A requested order line: which catalog item and how many.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for an order. Falls back to a placeholder when omitted.</summary>
public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>A line of a placed order, echoed back with the price that was captured.</summary>
public class OrderLineResultDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderLineResultDto From(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };
}

/// <summary>An order plus its payment state, as returned by the order endpoints.</summary>
public class OrderPaymentStateDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentStatus { get; set; } = nameof(OrderPaymentStatus.AwaitingPayment);
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }
    public List<OrderLineResultDto> Items { get; set; } = new();

    public static OrderPaymentStateDto From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = "USD",
        PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        CaptureId = order.PaymentCaptureId,
        RefundId = order.PaymentRefundId,
        Items = order.OrderItems.Select(OrderLineResultDto.From).ToList()
    };
}
