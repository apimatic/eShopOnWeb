using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe descriptor of the instrument that paid — never full card details.</summary>
public record CardDescriptorDto(string? Brand, string? Last4);

public record RefundView(string RefundId, decimal Amount, string Status, string IdempotencyKey, DateTimeOffset CreatedDate);

/// <summary>The payment state of an order, including the PayPal ids and current statuses.</summary>
public class PaymentDto
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? InvoiceId { get; init; }

    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }

    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }

    public CardDescriptorDto? Card { get; init; }

    public decimal TotalRefunded { get; init; }
    public decimal RefundableRemaining { get; init; }
    public IReadOnlyList<RefundView> Refunds { get; init; } = Array.Empty<RefundView>();

    public static PaymentDto From(Payment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        InvoiceId = p.InvoiceId,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        Card = (p.CardBrand is null && p.CardLast4 is null) ? null : new CardDescriptorDto(p.CardBrand, p.CardLast4),
        TotalRefunded = p.TotalRefunded(),
        RefundableRemaining = p.RefundableRemaining(),
        Refunds = p.Refunds
            .Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.IdempotencyKey, r.CreatedDate))
            .ToList()
    };
}

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order with its items and payment state, as returned by GET /api/my-orders.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public IReadOnlyList<OrderItemDto> Items { get; init; } = Array.Empty<OrderItemDto>();
    public PaymentDto? Payment { get; init; }

    public static OrderSummaryDto From(Order order, Payment? payment, string fallbackCurrency) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = payment?.CurrencyCode ?? fallbackCurrency,
        PaymentStatus = payment?.Status.ToString()
            ?? ApplicationCore.Entities.PaymentAggregate.PaymentStatus.AwaitingPayment.ToString(),
        Items = order.OrderItems
            .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList(),
        Payment = payment is null ? null : PaymentDto.From(payment)
    };
}

public record SavedCardDto(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? CardholderName, DateTimeOffset CreatedDate)
{
    public static SavedCardDto From(SavedCard c) =>
        new(c.Id, c.Brand, c.Last4, c.Expiry, c.CardholderName, c.CreatedDate);
}

// ------------------------------------------------------------------ response envelopes (top-level identifiers)

public class PlaceOrderResponse
{
    public int OrderId { get; init; }
    public PaymentDto? Payment { get; init; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; init; }
    public SavedCardDto? Card { get; init; }
}

public class RefundResponse
{
    public string RefundId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public PaymentDto? Payment { get; init; }
}
