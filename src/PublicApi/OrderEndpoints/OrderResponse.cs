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
    public string? Currency { get; set; }
    public PaymentStateResponse Payment { get; set; } = new();
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();

    public static OrderResponse From(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Payment.Currency,
        Payment = PaymentStateResponse.From(order),
        Items = order.OrderItems.Select(i => new OrderItemResponse
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Refunds = order.Refunds.Select(RefundResponse.From).ToList()
    };
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateResponse
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }

    public static PaymentStateResponse From(Order order) => new()
    {
        PayPalOrderId = order.Payment.PayPalOrderId,
        PayPalOrderStatus = order.Payment.PayPalOrderStatus,
        AuthorizationId = order.Payment.AuthorizationId,
        AuthorizationStatus = order.Payment.AuthorizationStatus,
        AuthorizationExpiration = order.Payment.AuthorizationExpiration,
        CaptureId = order.Payment.CaptureId,
        CaptureStatus = order.Payment.CaptureStatus,
        CapturedAmount = order.Payment.CapturedAmount,
        PaypalFee = order.Payment.PaypalFee,
        NetAmount = order.Payment.NetAmount,
        RefundedAmount = order.Payment.RefundedAmount,
        RemainingRefundable = order.Payment.RemainingRefundable
    };
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public static RefundResponse From(OrderRefund refund) => new()
    {
        RefundId = refund.PaypalRefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey
    };
}
