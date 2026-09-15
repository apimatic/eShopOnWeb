using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>Result of creating + authorizing a PayPal order (holding the funds).</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? InstrumentDescription);

/// <summary>Current state of an authorization as PayPal reports it.</summary>
public record PayPalAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime);

/// <summary>Result of capturing an authorization (taking the money).</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>Result of refunding a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of vaulting a card. Only ever the safe representation, never the PAN.</summary>
public record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction as reported by PayPal's Transaction Search.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
