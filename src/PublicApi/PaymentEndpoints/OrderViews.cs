using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe, shopper-facing view of an order and its payment state.</summary>
public class OrderView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderLineView> Items { get; set; } = new();
    public PaymentView? Payment { get; set; }

    public static OrderView From(Order order, string currency)
    {
        return new OrderView
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? currency,
            Items = order.OrderItems.Select(i => new OrderLineView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : PaymentView.From(order.Payment)
        };
    }
}

public class OrderLineView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The PayPal-owned payment state that a later request can act on.</summary>
public class PaymentView
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string ProviderOrderId { get; set; } = string.Empty;

    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundView> Refunds { get; set; } = new();

    public static PaymentView From(OrderPayment payment)
    {
        return new PaymentView
        {
            Amount = payment.Amount,
            Currency = payment.Currency,
            ProviderOrderId = payment.ProviderOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(RefundView.From)
                .ToList()
        };
    }
}

public class RefundView
{
    public int RefundId { get; set; }
    public string ProviderRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundView From(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        ProviderRefundId = refund.ProviderRefundId,
        Amount = refund.Amount,
        Currency = refund.Currency,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };
}
