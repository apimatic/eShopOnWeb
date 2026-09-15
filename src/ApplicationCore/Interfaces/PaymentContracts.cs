using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and quantity to include in an order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>An optional shipping address supplied when placing an order.</summary>
public record ShippingAddressInput(
    string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>
/// How to pay for an order: either raw <paramref name="Card"/> details for a one-off payment,
/// or a <paramref name="SavedPaymentMethodId"/> naming one of the shopper's saved cards.
/// Exactly one must be provided.
/// </summary>
public record AuthorizePaymentInput(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>
/// A refund request. The idempotency key makes a repeat under the same key a no-op, while two
/// distinct partial refunds (distinct keys) of the same capture remain legitimate.
/// </summary>
public record RefundInput(string IdempotencyKey, decimal? Amount);

/// <summary>Details needed to save a card to the shopper's vault.</summary>
public record SaveCardInput(CardDetails Card, string? Alias);

// ---- Reconciliation report ----

public record ReconciliationMatch(
    string TransactionId, int OrderId, decimal PayPalAmount, decimal OrderCapturedAmount,
    string? TransactionStatus, DateTimeOffset? TransactionDate);

public record ReconciliationPayPalOnly(
    string TransactionId, decimal Amount, string? Status, string? InvoiceId,
    string? CustomField, DateTimeOffset? Date);

public record ReconciliationEShopOnly(
    int OrderId, string CaptureId, decimal CapturedAmount, DateTimeOffset? CapturedAt);

public record ReconciliationTotals(
    int PayPalTransactionCount, int EShopCapturedOrderCount, int MatchedCount,
    int PayPalOnlyCount, int EShopOnlyCount);

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop's captured
/// orders, surfacing anything present on one side but not the other.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotInPayPal,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalNotInEShop,
    ReconciliationTotals Totals);
