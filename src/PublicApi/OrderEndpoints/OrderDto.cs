using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto Payment { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderPaymentDto
{
    public string? PayPalOrderId { get; set; }
    public OrderAuthorizationDto? Authorization { get; set; }
    public OrderCaptureDto? Capture { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderAuthorizationDto
{
    public string? Id { get; set; }
    public string? OriginalId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class OrderCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public static class OrderDtoMapper
{
    public static OrderDto From(Order order, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.PaymentCurrency ?? currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new OrderPaymentDto
            {
                PayPalOrderId = order.PayPalOrderId,
                Authorization = string.IsNullOrEmpty(order.PayPalAuthorizationId)
                    ? null
                    : new OrderAuthorizationDto
                    {
                        Id = order.PayPalAuthorizationId,
                        OriginalId = order.PayPalOriginalAuthorizationId,
                        Status = order.PayPalAuthorizationStatus,
                        ExpirationTime = order.AuthorizationExpiration
                    },
                Capture = string.IsNullOrEmpty(order.PayPalCaptureId)
                    ? null
                    : new OrderCaptureDto
                    {
                        Id = order.PayPalCaptureId,
                        Status = order.PayPalCaptureStatus,
                        Amount = order.CapturedAmount,
                        PayPalFee = order.PayPalFee,
                        NetAmount = order.NetProceeds
                    },
                Refunds = order.Refunds.Select(r => new OrderRefundDto
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
