using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderResponseMapper
{
    public static OrderResponse Map(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            CaptureId = order.CaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RemainingRefundable = order.RemainingRefundable(),
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new OrderRefundResponse
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<OrderRefundResponse> Refunds { get; set; } = new();
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderRefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
