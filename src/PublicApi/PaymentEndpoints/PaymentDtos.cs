using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Payment state returned by pay/fulfil/cancel. Carries the PayPal-owned ids and statuses.</summary>
public record PaymentStateResponse(
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? CardBrand,
    string? CardLast4,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    string? LastError);

/// <summary>A saved card described safely enough to recognise, never full card details.</summary>
public record SavedCardResponse(
    int PaymentMethodId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedDate);

public static class PaymentMappings
{
    public static PaymentStateResponse ToState(OrderPayment p) => new(
        OrderId: p.OrderId,
        Status: p.Status.ToString(),
        Amount: p.Amount,
        Currency: p.CurrencyCode,
        PayPalOrderId: p.PayPalOrderId,
        AuthorizationId: p.AuthorizationId,
        AuthorizationStatus: p.AuthorizationStatus,
        AuthorizationExpiresAt: p.AuthorizationExpiresAt,
        CaptureId: p.CaptureId,
        CapturedAmount: p.CapturedAmount,
        PayPalFee: p.PayPalFee,
        NetAmount: p.NetAmount,
        CardBrand: p.CardBrand,
        CardLast4: p.CardLast4,
        TotalRefunded: p.TotalRefunded(),
        RefundableRemaining: p.RefundableRemaining(),
        LastError: p.LastError);

    public static SavedCardResponse ToSavedCard(SavedPaymentMethod m) => new(
        PaymentMethodId: m.Id,
        Brand: m.CardBrand,
        Last4: m.CardLast4,
        Expiry: m.CardExpiry,
        CardholderName: m.CardholderName,
        CreatedDate: m.CreatedDate);
}
