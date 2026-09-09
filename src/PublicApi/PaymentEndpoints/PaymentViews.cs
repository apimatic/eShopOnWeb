using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe, read-only projections of the payment domain for API responses.</summary>
public class OrderView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public List<OrderItemView> Items { get; set; } = new();
    public PaymentView Payment { get; set; } = new();

    public static OrderView From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Currency = order.Payment.Currency,
        Total = order.Total(),
        PaymentStatus = order.Payment.Status.ToString(),
        Items = order.OrderItems.Select(i => new OrderItemView
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentView.From(order.Payment)
    };
}

public class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentView
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? Instrument { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundView> Refunds { get; set; } = new();

    public static PaymentView From(OrderPayment p) => new()
    {
        Status = p.Status.ToString(),
        Currency = p.Currency,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        Instrument = p.InstrumentDescription,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        RefundedAmount = p.RefundedAmount,
        RefundableAmount = p.RefundableAmount,
        Refunds = p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
    };
}

public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodView From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        LastFour = pm.CardLastFour,
        Expiry = pm.CardExpiry,
        CardholderName = pm.CardholderName,
        CreatedAt = pm.CreatedAt
    };
}
