using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

// ---- Inputs ----

/// <summary>A catalog item and quantity requested when placing an order.</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay for an order: either raw <see cref="Card"/> details for a one-off payment, or the id of one
/// of the shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PayInput(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A refund request. <see cref="Amount"/> null means refund the full remaining amount.</summary>
public record RefundInput(decimal? Amount, string IdempotencyKey, string? Reason);

// ---- Outputs ----

/// <summary>The outcome of placing an order: its id and the amount now awaiting payment.</summary>
public record PlacedOrder(int OrderId, decimal Amount, string Currency, string PaymentReference, string Status);

public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt, string? Reason);

/// <summary>The full payment state for an order, surfaced through the API.</summary>
public record PaymentView(
    int OrderId,
    string Status,
    string Currency,
    decimal Amount,
    string PaymentReference,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    string? PaymentSourceDescription,
    IReadOnlyList<RefundView> Refunds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CanceledAt);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    PaymentView? Payment);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

// ---- Reconciliation ----

/// <summary>A PayPal transaction that lined up with an eShop order.</summary>
public record MatchedReconciliationEntry(
    int OrderId,
    string? PaymentReference,
    string TransactionId,
    string TransactionStatus,
    decimal PayPalAmount,
    decimal EShopAmount,
    bool AmountMatches,
    string? EventCode,
    DateTimeOffset TransactionDate);

/// <summary>A transaction PayPal knows about that no eShop order claims.</summary>
public record PayPalOnlyEntry(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    string Status,
    DateTimeOffset Date,
    string? EventCode);

/// <summary>An eShop payment with money activity that no PayPal transaction (yet) reflects in the range.</summary>
public record EShopOnlyEntry(
    int OrderId,
    string PaymentReference,
    string PaymentStatus,
    decimal Amount,
    string? CaptureId,
    string? AuthorizationId);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalPayPalTransactions,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<MatchedReconciliationEntry> Matched,
    IReadOnlyList<PayPalOnlyEntry> PayPalOnly,
    IReadOnlyList<EShopOnlyEntry> EShopOnly);
