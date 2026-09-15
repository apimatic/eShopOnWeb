using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderPaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<PaymentRefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto Payment { get; set; } = new();
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Payment = ToPaymentDto(order.Payment)
        };

        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        return dto;
    }

    private static OrderPaymentDto ToPaymentDto(OrderPayment payment)
    {
        var dto = new OrderPaymentDto
        {
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationCreatedAt = payment.AuthorizationCreatedAt,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetProceeds = payment.NetProceeds,
            RefundedAmount = payment.RefundedAmount,
            RemainingRefundable = payment.RemainingRefundable
        };

        foreach (var refund in payment.Refunds)
        {
            dto.Refunds.Add(new PaymentRefundDto
            {
                RefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status
            });
        }

        return dto;
    }
}
