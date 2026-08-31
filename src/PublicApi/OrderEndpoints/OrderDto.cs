using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    public PaymentDto? Payment { get; set; }

    public static OrderDto FromOrder(Order order, Payment? payment)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payment == null ? null : PaymentDto.FromPayment(payment)
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

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? FailureReason { get; set; }
    public List<RefundDto> Refunds { get; set; } = new List<RefundDto>();

    public static PaymentDto FromPayment(Payment payment)
    {
        return new PaymentDto
        {
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            RefundableAmount = payment.RefundableAmount,
            FailureReason = payment.FailureReason,
            Refunds = payment.Refunds.Select(RefundDto.FromRefund).ToList()
        };
    }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto FromRefund(PaymentRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            IdempotencyKey = refund.IdempotencyKey,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status,
            CreatedAt = refund.CreatedAt
        };
    }
}
