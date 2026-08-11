using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of authorizing (placing a hold) via PayPal Orders v2.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Result of authorizing by card, which may additionally return a vault id when the card was
/// stored during the payment.
/// </summary>
public record CardAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? VaultId,
    string? CardBrand,
    string? Last4);

/// <summary>Result of capturing an authorization at fulfilment.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of vaulting (saving) a card for later reuse.</summary>
public record VaultCardResult(
    string VaultId,
    string? CardBrand,
    string? Last4,
    string? ExpiryMonth,
    string? ExpiryYear,
    string? CardholderName);

/// <summary>Result of refunding a captured payment.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>One PayPal transaction as reported by transaction search, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? Date,
    string? EventCode);
