using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A read model of an order and its payment state, safe to return to callers.</summary>
public class OrderView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Total { get; set; }

    /// <summary>Payment lifecycle: AwaitingPayment, Authorized, Captured, Voided, Refunded, PartiallyRefunded.</summary>
    public string PaymentStatus { get; set; } = "AwaitingPayment";

    public PaymentView? Payment { get; set; }
    public List<OrderItemView> Items { get; set; } = new();

    public static OrderView From(Order order)
    {
        return new OrderView
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            CurrencyCode = order.Payment?.CurrencyCode ?? string.Empty,
            Total = order.Total(),
            PaymentStatus = order.Payment?.Status.ToString() ?? "AwaitingPayment",
            Payment = order.Payment is null ? null : PaymentView.From(order.Payment),
            Items = order.OrderItems.Select(OrderItemView.From).ToList()
        };
    }
}

public class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderItemView From(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };
}

/// <summary>The PayPal-owned payment state for an order.</summary>
public class PaymentView
{
    public string Provider { get; set; } = "PayPal";
    public string Status { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CardDescriptor { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    public string PayPalOrderId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
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

    public static PaymentView From(OrderPayment p) => new()
    {
        Provider = p.Provider,
        Status = p.Status.ToString(),
        CurrencyCode = p.CurrencyCode,
        Amount = p.Amount,
        CardDescriptor = p.CardDescriptor,
        SavedPaymentMethodId = p.SavedPaymentMethodId,
        PayPalOrderId = p.PayPalOrderId,
        InvoiceId = p.InvoiceId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded,
        RefundableRemaining = p.RefundableRemaining,
        Refunds = p.Refunds.Select(RefundView.From).ToList()
    };
}

public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundView From(PaymentRefund r) => new()
    {
        RefundId = r.RefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

/// <summary>A saved card described safely for the shopper (never full card details).</summary>
public class PaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodView From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        CreatedDate = pm.CreatedDate
    };
}
