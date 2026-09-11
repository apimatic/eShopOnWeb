using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The result of authorizing (placing a hold on) a shopper's funds. Carries the identifiers and
/// statuses that PayPal owns so later requests (capture, void, reauthorize) can act on them.
/// </summary>
public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>The result of capturing (taking) a previously authorized payment.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    Money Gross,
    Money? Fee,
    Money? Net);

/// <summary>The result of renewing a stale authorization.</summary>
public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The result of refunding a captured payment, in full or in part.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    Money Amount);

/// <summary>The result of vaulting a card. Describes the card safely; never carries the full number.</summary>
public record VaultCardResult(
    string VaultTokenId,
    string? PayPalCustomerId,
    string CardBrand,
    string CardLast4,
    string CardExpiry);

/// <summary>
/// One transaction as PayPal's transaction-search reporting knows it, used to reconcile against
/// eShop orders.
/// </summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? EventCode,
    Money? Amount,
    Money? Fee,
    DateTimeOffset? InitiationDate);
