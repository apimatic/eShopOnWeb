using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderPaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public OrderPaymentDto? Payment { get; set; }

    public static OrderDto FromOrder(Order order) => new OrderDto
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Currency,
        OrderDate = order.OrderDate,
        Payment = order.Payment is null
            ? null
            : new OrderPaymentDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpiresAt = order.Payment.AuthorizationExpiresAt,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PayPalFeeAmount = order.Payment.PayPalFeeAmount,
                NetAmount = order.Payment.NetAmount,
                RefundedAmount = order.Payment.RefundedAmount
            }
    };
}
