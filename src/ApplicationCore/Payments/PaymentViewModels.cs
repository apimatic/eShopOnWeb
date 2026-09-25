using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of an order to place: a catalog item and how many.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order (defaults are used when omitted).</summary>
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>A refund as the app records it.</summary>
public sealed record RefundLine(int Id, string? PayPalRefundId, decimal Amount, string? Status, DateTimeOffset CreatedAt);

/// <summary>An order together with its payment state — the shape returned by my-orders and the pay/fulfil/cancel/refund actions.</summary>
public sealed record OrderPaymentSummary(
    int OrderId,
    string State,
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
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
    decimal RefundableAmount,
    string? LastError,
    IReadOnlyList<RefundLine> Refunds);

/// <summary>Response to a refund request: the created refund's id plus the updated order payment.</summary>
public sealed record RefundResponse(string RefundId, OrderPaymentSummary Payment);

/// <summary>A safe description of a saved card — never full card details.</summary>
public sealed record SavedCardInfo(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

/// <summary>A PayPal transaction lined up against an eShop order for the same invoice reference.</summary>
public sealed record ReconciliationMatch(
    string InvoiceId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    int EshopOrderId,
    decimal EshopAmount,
    string EshopState,
    bool AmountsAgree);

/// <summary>A PayPal transaction with no matching eShop order.</summary>
public sealed record PayPalOnlyTransaction(string? TransactionId, string? InvoiceId, decimal? Amount, string? CurrencyCode, string? Status, DateTimeOffset? InitiationDate);

/// <summary>An eShop order that reached PayPal (was paid) but has no matching PayPal transaction in the range.</summary>
public sealed record EshopOnlyPayment(int OrderId, string InvoiceId, decimal Amount, string State, string? PayPalOrderId);

/// <summary>The reconciliation report over a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Complete,
    int PagesFetched,
    int TotalPages,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalOnlyTransaction> OnlyInPayPal,
    IReadOnlyList<EshopOnlyPayment> OnlyInEshop);
