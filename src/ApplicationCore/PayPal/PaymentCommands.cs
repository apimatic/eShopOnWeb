using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>One line of a place-order request: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order: either raw card details for a one-off payment, or the id of one of the shopper's
/// saved cards. Exactly one must be supplied.
/// </summary>
public record PayCommand(CardDetails? Card, int? SavedCardId);

/// <summary>An order paired with its payment state (payment may be null when awaiting payment).</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>One line of the reconciliation report: a PayPal transaction and/or an eShop payment.</summary>
public record ReconciliationEntry(
    string? InvoiceId,
    string Match,               // "matched", "only-in-paypal", or "only-in-eshop"
    int? OrderId,
    decimal? EShopAmount,
    string? EShopStatus,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus);

/// <summary>The reconciliation report over a date range, lining PayPal's record up against eShop's.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries,
    int MatchedCount,
    int OnlyInPayPalCount,
    int OnlyInEShopCount,
    int PayPalTransactionsScanned,
    bool Truncated);
