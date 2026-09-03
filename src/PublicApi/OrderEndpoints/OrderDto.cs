using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

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
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalInvoiceId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalAuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalCaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();

    public static OrderDto From(Order order) =>
        new()
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            PayPalOrderId = order.PayPalOrderId,
            PayPalInvoiceId = order.PayPalInvoiceId,
            PayPalAuthorizationId = order.PayPalAuthorizationId,
            PayPalAuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            PayPalCaptureId = order.PayPalCaptureId,
            PayPalCaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RemainingRefundable = order.RemainingRefundable(),
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
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency
            }).ToList()
        };
}
