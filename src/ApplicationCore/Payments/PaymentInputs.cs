using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Currency the payment flow charges in, sourced once from <c>PayPal:Currency</c>.</summary>
public sealed class PaymentOptions
{
    public string Currency { get; init; } = "USD";
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public sealed record ShippingAddressRequest(
    string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>
/// How to pay an order: either raw <see cref="Card"/> details for a one-off payment, or the id of one of
/// the caller's saved cards. Exactly one should be supplied.
/// </summary>
public sealed record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A single line in the reconciliation report.</summary>
public sealed record ReconciliationLine(
    int? OrderId,
    string? OrderReference,
    string? PayPalTransactionId,
    string? EshopCaptureId,
    decimal? PayPalAmount,
    decimal? EshopAmount,
    string? Currency,
    string? Status);

/// <summary>
/// Reconciliation of PayPal's own transaction record against eShop orders across a date range, so a
/// payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> InPayPalNotInEshop,
    IReadOnlyList<ReconciliationLine> InEshopNotInPayPal);
