using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? OrderDate { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentResponse
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CreateRefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal RemainingRefundable { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

internal static class OrderResponseMapper
{
    public static OrderResponse From(OrderPaymentResult result) => new()
    {
        OrderId = result.OrderId,
        Status = result.Status.ToString(),
        Total = result.OrderTotal,
        Currency = result.Currency,
        Items = result.Items.Select(From).ToList(),
        Payment = From(result.Payment)
    };

    public static OrderResponse From(ShopperOrderResult result) => new()
    {
        OrderId = result.OrderId,
        Status = result.Status.ToString(),
        Total = result.Total,
        Currency = result.Currency,
        OrderDate = result.OrderDate,
        Items = result.Items.Select(From).ToList(),
        Payment = result.Payment is null ? null : From(result.Payment)
    };

    public static OrderResponse From(Order order, string currency) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = currency,
        OrderDate = order.OrderDate,
        Items = order.OrderItems.Select(i => new OrderItemResponse
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            Name = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList()
    };

    public static CreateRefundResponse From(RefundResult result) => new()
    {
        RefundId = result.RefundId,
        OrderId = result.OrderId,
        OrderStatus = result.OrderStatus.ToString(),
        Amount = result.Amount,
        Currency = result.Currency,
        RemainingRefundable = result.RemainingRefundable,
        Status = result.Status
    };

    private static OrderItemResponse From(OrderLineResult item) => new()
    {
        CatalogItemId = item.CatalogItemId,
        Name = item.Name,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity
    };

    private static PaymentResponse From(PaymentStateResult payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        PayPalOrderStatus = payment.PayPalOrderStatus,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiration = payment.AuthorizationExpiration,
        AuthorizedAmount = payment.AuthorizedAmount,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        Refunds = payment.Refunds.Select(r => new RefundResponse
        {
            RefundId = r.RefundId,
            Status = r.Status,
            Amount = r.Amount,
            Currency = r.Currency,
            IdempotencyKey = r.IdempotencyKey
        }).ToList()
    };
}
