using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ---- Shared request shapes ----

/// <summary>Raw card details for a one-off payment or for saving. Never stored or logged by this app.</summary>
public record CardRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddressRequest? BillingAddress);

public record BillingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string PostalCode,
    string CountryCode);

// ---- Shared response shapes ----

/// <summary>A safe descriptor of the instrument used — brand and last four only.</summary>
public record CardSummaryDto(string? Brand, string? Last4);

public record RefundDto(int RefundId, string PayPalRefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

/// <summary>The full payment state for an order, as returned by pay/fulfil/cancel/my-orders.</summary>
public record OrderPaymentDto(
    int OrderId,
    string OrderStatus,
    string PaymentState,
    decimal Total,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal? RefundedTotal,
    decimal? RefundableRemaining,
    CardSummaryDto? Card,
    int? SavedCardId,
    IReadOnlyList<RefundDto> Refunds);

/// <summary>Maps domain entities to the API response shapes. Never emits card numbers.</summary>
public static class PaymentDtoMapper
{
    public static CardDetails ToCardDetails(CardRequest card) => new(
        card.Number, card.Expiry, card.SecurityCode, card.Name,
        card.BillingAddress is null
            ? null
            : new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));

    public static OrderPaymentDto ToDto(Order order, string currency)
    {
        var payment = order.Payment;
        var refunds = payment?.Refunds
            .Select(r => new RefundDto(r.Id, r.PayPalRefundId, r.Amount, r.Currency, r.Status, r.CreatedAt))
            .ToList() ?? new List<RefundDto>();

        return new OrderPaymentDto(
            OrderId: order.Id,
            OrderStatus: order.Status.ToString(),
            PaymentState: DescribePaymentState(order),
            Total: order.Total(),
            Currency: payment?.Currency ?? currency,
            PayPalOrderId: payment?.PayPalOrderId,
            AuthorizationId: payment?.AuthorizationId,
            AuthorizationStatus: payment?.AuthorizationStatus,
            AuthorizationExpiresAt: payment?.AuthorizationExpiresAt,
            CaptureId: payment?.CaptureId,
            CaptureStatus: payment?.CaptureStatus,
            CapturedAmount: payment?.CapturedAmount,
            PayPalFee: payment?.PayPalFee,
            NetAmount: payment?.NetAmount,
            RefundedTotal: payment is null ? null : payment.TotalRefunded,
            RefundableRemaining: payment is null ? null : payment.RefundableRemaining,
            Card: payment is null ? null : new CardSummaryDto(payment.CardBrand, payment.CardLast4),
            SavedCardId: payment?.SavedCardId,
            Refunds: refunds);
    }

    /// <summary>A shopper-friendly summary of where the money is.</summary>
    private static string DescribePaymentState(Order order)
    {
        var payment = order.Payment;
        return order.Status switch
        {
            OrderStatus.AwaitingPayment => "AwaitingPayment",
            OrderStatus.PaymentAuthorized => "Authorized",
            OrderStatus.Cancelled => "Cancelled",
            OrderStatus.Fulfilled when payment is not null && payment.TotalRefunded <= 0 => "Captured",
            OrderStatus.Fulfilled when payment is not null && payment.RefundableRemaining <= 0 => "Refunded",
            OrderStatus.Fulfilled when payment is not null => "PartiallyRefunded",
            OrderStatus.Fulfilled => "Fulfilled",
            _ => order.Status.ToString()
        };
    }
}
