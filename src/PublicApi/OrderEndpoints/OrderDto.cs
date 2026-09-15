using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public OrderPaymentDto? Payment { get; set; }
    public OrderItemDto[] Items { get; set; } = Array.Empty<OrderItemDto>();

    public static OrderDto From(Order order, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.FulfillmentStatus.ToString(),
            Total = order.Total(),
            Currency = order.PaymentCurrency ?? currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToArray(),
            Payment = order.PayPalOrderId is null && order.PayPalAuthorizationId is null && order.PayPalCaptureId is null
                ? null
                : new OrderPaymentDto
                {
                    PayPalOrderId = order.PayPalOrderId,
                    PayPalOrderStatus = order.PayPalOrderStatus,
                    AuthorizationId = order.PayPalAuthorizationId,
                    AuthorizationStatus = order.PayPalAuthorizationStatus,
                    AuthorizationExpiration = order.PayPalAuthorizationExpiration,
                    CaptureId = order.PayPalCaptureId,
                    CaptureStatus = order.PayPalCaptureStatus,
                    CapturedAmount = order.CapturedAmount,
                    PaypalFee = order.PaypalFee,
                    NetAmount = order.NetAmount,
                    RemainingRefundable = order.RemainingRefundable(),
                    Refunds = order.Refunds.Select(r => new OrderRefundDto
                    {
                        RefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        Status = r.Status,
                        CreatedAt = r.CreatedAt
                    }).ToArray()
                }
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderPaymentDto
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
    public decimal RemainingRefundable { get; set; }
    public OrderRefundDto[] Refunds { get; set; } = Array.Empty<OrderRefundDto>();
}

public class OrderRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
