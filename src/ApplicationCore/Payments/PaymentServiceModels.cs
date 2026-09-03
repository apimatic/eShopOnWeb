using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A catalog item and quantity to place on an order.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for an order.</summary>
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay for an order: either raw <see cref="Card"/> details for a one-off payment, or the id of
/// one of the shopper's saved cards. Exactly one must be supplied.
/// </summary>
public sealed record PayInstruction(CardInput? Card, int? SavedCardId);

/// <summary>A line on an order, for display.</summary>
public sealed record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order joined with its payment state — what the shopper sees in "my orders".</summary>
public sealed record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    string PaymentStatus,
    string CurrencyCode,
    decimal Total,
    string InvoiceId,
    string? PayPalOrderId,
    string? AuthorizationId,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RemainingRefundable,
    IReadOnlyList<OrderLineView> Items);

/// <summary>Outcome of a refund request.</summary>
public sealed record RefundOutcome(
    int RefundId,
    string PayPalRefundId,
    decimal Amount,
    string RefundStatus,
    decimal TotalRefunded,
    string PaymentStatus);

/// <summary>A saved card, described safely for the shopper.</summary>
public sealed record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

/// <summary>Which side(s) of reconciliation a line came from.</summary>
public enum ReconciliationMatch
{
    Matched = 0,
    PayPalOnly = 1,
    EShopOnly = 2
}

/// <summary>One reconciled line: a PayPal transaction, an eShop order, or both lined up by invoice id.</summary>
public sealed record ReconciliationLine(
    ReconciliationMatch Match,
    string? InvoiceId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    int? EShopOrderId,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

/// <summary>The reconciliation report for a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
