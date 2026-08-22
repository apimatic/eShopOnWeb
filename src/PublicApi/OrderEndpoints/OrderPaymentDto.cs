using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalAuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalCaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto From(Order order)
    {
        return new OrderPaymentDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            PayPalOrderId = order.PayPalOrderId,
            PayPalAuthorizationId = order.PayPalAuthorizationId,
            PayPalAuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            PayPalCaptureId = order.PayPalCaptureId,
            PayPalCaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RefundableRemaining = order.RefundableRemaining(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new OrderRefundDto
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

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
