using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateDto? Payment { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public static class OrderDtoMapper
{
    public static OrderDto FromOrder(Order order, string? configuredCurrency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.RoundedTotal(),
            Currency = order.Currency ?? configuredCurrency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = order.PayPalOrderId is null && order.PayPalAuthorizationId is null && order.PayPalCaptureId is null
                ? null
                : new PaymentStateDto
                {
                    PayPalOrderId = order.PayPalOrderId,
                    AuthorizationId = order.PayPalAuthorizationId,
                    AuthorizationStatus = order.PayPalAuthorizationStatus,
                    AuthorizationExpiration = order.AuthorizationExpiration,
                    CaptureId = order.PayPalCaptureId,
                    CaptureStatus = order.PayPalCaptureStatus,
                    CapturedAmount = order.CapturedAmount,
                    PaypalFee = order.PaypalFee,
                    NetAmount = order.NetAmount,
                    RefundedAmount = order.RefundedTotal(),
                    RefundableRemaining = order.RefundableRemaining(),
                    Refunds = order.Refunds.Select(r => new RefundDto
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
}
