using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>A caller's order together with its payment state.</summary>
public record OrderPaymentView(
    int OrderId,
    string BuyerId,
    decimal Total,
    string Currency,
    string PaymentStatus,
    DateTimeOffset OrderDate,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableAmount,
    string? CardBrand,
    string? CardLast4,
    IReadOnlyList<OrderLineView> Items,
    IReadOnlyList<RefundView> Refunds);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record RefundView(int RefundId, string PayPalRefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public record SavedCardView(int PaymentMethodId, string Brand, string Last4, string Expiry,
    string? CardholderName, string? CardType, DateTimeOffset CreatedAt);

/// <summary>
/// Reconciliation report lining PayPal's own transaction record up against eShop orders.
/// A transaction PayPal knows about but eShop does not — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

public record ReconciliationLine(
    string MatchState, // "Matched", "PayPalOnly", "EShopOnly"
    string? TransactionId,
    string? EventCode,
    string? TransactionStatus,
    decimal? PayPalAmount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    int? OrderId,
    string? OrderPaymentStatus,
    decimal? OrderAmount,
    DateTimeOffset? Date);
