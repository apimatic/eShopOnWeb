using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderResponseMapper
{
    public static OrderResponse Map(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.PaymentCurrency,
        PayPalOrderId = order.PayPalOrderId,
        AuthorizationId = order.PayPalAuthorizationId,
        AuthorizationStatus = order.AuthorizationStatus,
        AuthorizationExpiration = order.AuthorizationExpiration,
        CaptureId = order.PayPalCaptureId,
        CaptureStatus = order.CaptureStatus,
        CapturedAmount = order.CapturedAmount,
        PaypalFee = order.PaypalFee,
        NetAmount = order.NetAmount,
        RemainingRefundable = order.RemainingRefundable(),
        Items = MapItems(order),
        Refunds = MapRefunds(order)
    };

    private static List<OrderItemResponse> MapItems(Order order)
    {
        var items = new List<OrderItemResponse>();
        foreach (var item in order.OrderItems)
        {
            items.Add(new OrderItemResponse
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        return items;
    }

    private static List<RefundResponse> MapRefunds(Order order)
    {
        var refunds = new List<RefundResponse>();
        foreach (var refund in order.Refunds)
        {
            refunds.Add(new RefundResponse
            {
                RefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Currency = refund.Currency,
                Status = refund.Status
            });
        }

        return refunds;
    }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public System.DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
