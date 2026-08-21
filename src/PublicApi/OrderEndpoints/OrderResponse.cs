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
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateResponse Payment { get; set; } = new();
    public List<OrderItemResponse> Items { get; set; } = new();
}

public class PaymentStateResponse
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
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public static class OrderResponseMapper
{
    public static OrderResponse From(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new PaymentStateResponse
            {
                PayPalOrderId = order.PayPalOrderId,
                PayPalOrderStatus = order.PayPalOrderStatus,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationExpiration = order.AuthorizationExpiration,
                AuthorizedAmount = order.AuthorizedAmount,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PayPalFee = order.PayPalFee,
                NetAmount = order.NetAmount,
                RefundedAmount = order.RefundedTotal(),
                RemainingRefundable = order.RemainingRefundable(),
                Refunds = order.Refunds.Select(r => new RefundResponse
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    Currency = r.Currency
                }).ToList()
            }
        };
    }

    public static PaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.LastDigits,
        Brand = method.Brand,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}
