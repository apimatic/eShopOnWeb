using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a shopper wants to pay: a one-off raw card, or one of their saved cards (exactly one).</summary>
public record PayInstruction(PayPalCardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A requested order line (catalog item + quantity).</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The outcome of placing an order: its id and the total that must be paid.</summary>
public record PlacedOrder(int OrderId, decimal Amount, string Currency);

public record RefundView(
    int RefundId,
    string PayPalRefundId,
    decimal Amount,
    string Currency,
    string Status,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

/// <summary>Read model describing a payment's full PayPal-owned state.</summary>
public record PaymentView(
    int OrderId,
    string BuyerId,
    string Currency,
    decimal Amount,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? CardBrand,
    string? CardLast4,
    decimal RefundedAmount,
    decimal RemainingRefundable,
    string? FailureReason,
    IReadOnlyList<RefundView> Refunds);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order together with its payment state, for the my-orders view.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    PaymentView? Payment);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);
