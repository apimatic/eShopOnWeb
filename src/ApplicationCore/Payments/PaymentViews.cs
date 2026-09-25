using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item id and quantity. Prices come from the catalog.</summary>
public record PlaceOrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>How to pay: a one-off card, or a saved card's id (exactly one).</summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

public record RefundView(string RefundId, decimal Amount, string Status);

/// <summary>The PayPal-owned state of an order's payment, surfaced for shopper/operator reads.</summary>
public record PaymentSummary(
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? PaymentMethod,
    IReadOnlyList<RefundView> Refunds);

/// <summary>An order with its payment state, for <c>GET /api/my-orders</c> and action responses.</summary>
public record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string PaymentStatus,
    decimal Total,
    string Currency,
    PaymentSummary? Payment);

public record SavedCardView(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, DateTimeOffset CreatedAt);

public record ReconciliationEntry(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    int? MatchedOrderId,
    string MatchState);   // "Matched", "PayPalOnly", "EShopOnly"

/// <summary>
/// PayPal's transactions for a range lined up against eShop orders. <see cref="Truncated"/> tells the
/// caller the transaction walk was cut short by a page cap.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries,
    int PayPalTransactionCount,
    int EShopOrderCount,
    bool Truncated,
    int PagesFetched,
    int? TotalPages);
