using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class PlaceOrderAddress
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public class OrderLineResult
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public class RefundResult
{
    public int RefundId { get; init; }
    public string PayPalRefundId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
}

public class OrderPaymentResult
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundableAmount { get; init; }
    public IReadOnlyList<OrderLineResult> Items { get; init; } = Array.Empty<OrderLineResult>();
    public IReadOnlyList<RefundResult> Refunds { get; init; } = Array.Empty<RefundResult>();
}

public class ReconciliationRow
{
    public string Match { get; init; } = string.Empty;
    public int? OrderId { get; init; }
    public string? OrderStatus { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public string? Note { get; init; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int PayPalTransactionCount { get; init; }
    public int EshopOrderCount { get; init; }
    public int MatchedCount { get; init; }
    public int PayPalOnlyCount { get; init; }
    public int EshopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationRow> Rows { get; init; } = Array.Empty<ReconciliationRow>();
}

public static class OrderPaymentMapper
{
    public static OrderPaymentResult ToResult(Order order)
    {
        return new OrderPaymentResult
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            CaptureId = order.PayPalCaptureId,
            CaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RefundableAmount = order.RefundableAmount(),
            Items = order.OrderItems.Select(i => new OrderLineResult
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new RefundResult
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                IdempotencyKey = r.IdempotencyKey
            }).ToList()
        };
    }
}
