using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderResponseMapper
{
    public static OrderResponse From(Order order, string currency)
    {
        var payment = order.Payment;
        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.Currency ?? currency,
            RemainingRefundableAmount = order.RemainingRefundableAmount(),
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = payment == null
                ? null
                : new PaymentResponse
                {
                    PayPalOrderId = payment.PayPalOrderId,
                    InvoiceId = payment.InvoiceId,
                    AuthorizationId = payment.AuthorizationId,
                    AuthorizationStatus = payment.AuthorizationStatus,
                    AuthorizationExpiration = payment.AuthorizationExpiration,
                    CaptureId = payment.CaptureId,
                    CaptureStatus = payment.CaptureStatus,
                    CapturedAmount = payment.CapturedAmount,
                    PayPalFee = payment.PayPalFee,
                    NetAmount = payment.NetAmount,
                    Refunds = order.PaymentRefunds.Select(r => new RefundResponse
                    {
                        RefundId = r.Id,
                        PayPalRefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        Status = r.Status
                    }).ToList()
                }
        };
    }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal RemainingRefundableAmount { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
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
    public string PayPalOrderId { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
