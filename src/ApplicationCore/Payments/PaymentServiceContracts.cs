using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of an order placement request: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public record ShippingAddressInput(
    string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>
/// How to pay: either a one-off <see cref="Card"/>, or the id of one of the shopper's saved cards. Exactly
/// one must be supplied.
/// </summary>
public record PaymentInstruction(CardDetails? Card, string? SavedPaymentMethodId);

/// <summary>An order paired with its payment state (null while awaiting payment) for <c>my-orders</c>.</summary>
public record MyOrder(Order Order, OrderPayment? Payment);

/// <summary>One reconciliation line: a PayPal transaction and/or the eShop order it lines up with.</summary>
public record ReconciliationLine(
    string Match,                 // "matched" | "paypal-only" | "eshop-only"
    string? TransactionId,
    string? InvoiceId,
    int? OrderId,
    string? OrderReference,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? Status);

public record ReconciliationSummary(int PayPalTransactions, int Matched, int PayPalOnly, int EShopOnly);

/// <summary>A reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    ReconciliationSummary Summary,
    IReadOnlyList<ReconciliationLine> Lines);
