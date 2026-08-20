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

    public static OrderResponse From(Order order, string currency)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.PaymentCurrency ?? currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = order.PaypalOrderId is null && order.PaypalAuthorizationId is null && order.PaypalCaptureId is null
                ? null
                : new PaymentResponse
                {
                    PaypalOrderId = order.PaypalOrderId,
                    AuthorizationId = order.PaypalAuthorizationId,
                    AuthorizationStatus = order.PaypalAuthorizationStatus,
                    AuthorizationExpiresAt = order.AuthorizationExpiresAt,
                    CaptureId = order.PaypalCaptureId,
                    CaptureStatus = order.PaypalCaptureStatus,
                    CapturedAmount = order.CapturedAmount,
                    PaypalFee = order.PaypalFee,
                    NetProceeds = order.NetProceeds,
                    Currency = order.PaymentCurrency,
                    RemainingRefundable = order.RemainingRefundable(),
                    Refunds = order.Refunds.Select(r => new RefundResponse
                    {
                        RefundId = r.PaypalRefundId,
                        Status = r.Status,
                        Amount = r.Amount,
                        Currency = r.Currency
                    }).ToList()
                }
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
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public string? Currency { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
