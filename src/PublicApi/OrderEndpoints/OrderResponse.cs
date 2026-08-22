using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }

    public static OrderResponse From(Order order, string fallbackCurrency)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency ?? fallbackCurrency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = PaymentResponse.From(order)
        };
    }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentResponse
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreated { get; set; }
    public DateTimeOffset? AuthorizationExpires { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();

    public static PaymentResponse? From(Order order)
    {
        if (string.IsNullOrEmpty(order.PayPalOrderId)
            && string.IsNullOrEmpty(order.PayPalAuthorizationId)
            && string.IsNullOrEmpty(order.PayPalCaptureId)
            && order.Refunds.Count == 0)
        {
            return null;
        }

        return new PaymentResponse
        {
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationCreated = order.PayPalAuthorizationCreated,
            AuthorizationExpires = order.PayPalAuthorizationExpires,
            CaptureId = order.PayPalCaptureId,
            CaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedTotal(),
            RefundableRemaining = order.RefundableRemaining(),
            Refunds = order.Refunds.Select(RefundResponse.From).ToList()
        };
    }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundResponse From(PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency,
        CreatedAt = refund.CreatedAt
    };
}
