using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A shopper's order with its current payment state, for the "my orders" view.</summary>
public record OrderPaymentView(
    int OrderId,
    string BuyerId,
    decimal Total,
    string Currency,
    string PaymentStatus,
    DateTimeOffset OrderDate,
    PaymentDetailView? Payment,
    IReadOnlyList<OrderLineView> Items);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record PaymentDetailView(
    string PayPalOrderId,
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? PaymentMethodDescription,
    IReadOnlyList<RefundView> Refunds);

public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public record SavedCardView(int PaymentMethodId, string Brand, string LastFourDigits, string? Expiry, DateTimeOffset CreatedAt);

/// <summary>The result of a refund, carrying the refund id and the resulting order state.</summary>
public record RefundResult(string RefundId, OrderPaymentView Order);

/// <summary>
/// A reconciliation report lining PayPal's own transaction records up against eShop orders over a
/// date range. Categories: Matched (present in both), PayPalOnly (PayPal has it, eShop doesn't),
/// EShopOnly (eShop captured it, not seen in PayPal's report for the range).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

public record ReconciliationLine(
    string Category,
    int? EShopOrderId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    decimal? EShopCapturedAmount,
    string? EShopPaymentStatus,
    string? Note);
