using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>How a shopper wants to pay: either one-off <see cref="Card"/> details, or a <see cref="SavedPaymentMethodId"/>.</summary>
public record PaymentInstrument(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A requested order line: a catalog item and quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order with its payment state — the shape returned by GET /api/my-orders.</summary>
public record OrderPaymentSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    PaymentStateView? Payment,
    IReadOnlyList<OrderLineView> Items);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>PayPal-owned payment state exposed to the caller (no card details).</summary>
public record PaymentStateView(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    decimal AuthorizedAmount,
    string PaymentMethodDescription,
    bool UsedSavedCard,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedGrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    IReadOnlyList<RefundView> Refunds);

public record RefundView(int Id, string PayPalRefundId, string Status, decimal Amount, string Currency);

/// <summary>A saved card described safely enough for the shopper to recognise it.</summary>
public record SavedPaymentMethodSummary(
    int Id,
    string CardBrand,
    string LastFourDigits,
    string Expiry,
    DateTimeOffset CreatedAt);

/// <summary>
/// A reconciliation report lining PayPal's own records up against eShop orders over a date range: matched
/// transactions, transactions PayPal knows about that eShop does not, and eShop captures PayPal has not
/// reported (yet).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationTransaction> MissingInEShop,
    IReadOnlyList<ReconciliationEShopEntry> MissingInPayPal);

public record ReconciliationMatch(
    int OrderId,
    string CaptureId,
    decimal EShopAmount,
    decimal? PayPalAmount,
    string PayPalStatus);

public record ReconciliationEShopEntry(
    int OrderId,
    string CaptureId,
    decimal Amount,
    string Currency,
    DateTimeOffset? CapturedAt);
