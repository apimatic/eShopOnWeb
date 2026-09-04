using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shared safe projection of an order's payment state (PayPal ids + money amounts).
/// Never contains card data.
/// </summary>
public class PaymentStateDto
{
    public string Status { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal AuthorizedAmount { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpirationTime { get; init; }
    public int AuthorizationGeneration { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? FeeAmount { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal RefundableAmount { get; init; }
    public IReadOnlyList<RefundDto> Refunds { get; init; } = Array.Empty<RefundDto>();

    public static PaymentStateDto From(Payment payment) => new PaymentStateDto
    {
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AuthorizedAmount = payment.AuthorizedAmount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpirationTime = payment.AuthorizationExpirationTime,
        AuthorizationGeneration = payment.AuthorizationGeneration,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        FeeAmount = payment.FeeAmount,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            Status = r.Status,
            Amount = r.Amount,
            RequestedTime = r.RequestedTime,
            CompletedTime = r.CompletedTime
        }).ToList()
    };
}

public class RefundDto
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset RequestedTime { get; init; }
    public DateTimeOffset? CompletedTime { get; init; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public class OrderSummaryDto
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public List<OrderLineDto> Items { get; init; } = new List<OrderLineDto>();
    public PaymentStateDto? Payment { get; init; }

    public static OrderSummaryDto From(Order order) => new OrderSummaryDto
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(oi => new OrderLineDto
        {
            CatalogItemId = oi.ItemOrdered.CatalogItemId,
            ProductName = oi.ItemOrdered.ProductName ?? string.Empty,
            UnitPrice = oi.UnitPrice,
            Units = oi.Units
        }).ToList(),
        Payment = order.Payment is null ? null : PaymentStateDto.From(order.Payment)
    };
}
