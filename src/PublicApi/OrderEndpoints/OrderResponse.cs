using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();

    public static OrderResponse From(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus,
            Total = order.Total(),
            Currency = order.Currency,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpirationTime = order.AuthorizationExpirationTime,
            CaptureId = order.CaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                ProductName = i.ItemOrdered.ProductName,
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(RefundResponse.From).ToList()
        };
    }
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
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public static RefundResponse From(OrderRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency
    };
}

public class PaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? CardType { get; set; }

    public static PaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.PaymentTokenId,
        LastDigits = method.LastDigits,
        Brand = method.Brand,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CardType = method.CardType
    };
}
