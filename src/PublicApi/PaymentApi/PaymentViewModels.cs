using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

/// <summary>A safe, read-only view of an order line.</summary>
public class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>A view of a single refund taken against a payment.</summary>
public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The money state PayPal owns for an order, shaped for the API.</summary>
public class PaymentView
{
    public string State { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }

    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public int? SavedPaymentMethodId { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
}

/// <summary>The full view of an order together with its payment state.</summary>
public class OrderView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemView> Items { get; set; } = new();
    public PaymentView? Payment { get; set; }
}

/// <summary>A safe view of a saved card (never full card details).</summary>
public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps the payment domain onto the API view models.</summary>
public static class PaymentViewMapper
{
    public static OrderView ToView(Order order, string fallbackCurrency)
    {
        return new OrderView
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? fallbackCurrency,
            Items = order.OrderItems.Select(i => new OrderItemView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : ToView(order.Payment)
        };
    }

    public static PaymentView ToView(Payment payment)
    {
        return new PaymentView
        {
            State = payment.State.ToString(),
            Currency = payment.Currency,
            AuthorizedAmount = payment.AuthorizedAmount,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedGrossAmount,
            PayPalFee = payment.PayPalFeeAmount,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
            SavedPaymentMethodId = payment.SavedPaymentMethodId,
            Refunds = payment.Refunds
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

    public static SavedCardView ToView(SavedPaymentMethod card)
    {
        return new SavedCardView
        {
            PaymentMethodId = card.Id,
            Brand = card.CardBrand,
            Last4 = card.CardLast4,
            Expiry = card.CardExpiry,
            CardholderName = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }
}
