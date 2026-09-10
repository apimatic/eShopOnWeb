using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Result of authorizing an order (creating a PayPal order and its authorization).</summary>
public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status);

/// <summary>Result of capturing an authorization, including PayPal's settlement breakdown.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of reauthorizing an authorization; the id is a new authorization id.</summary>
public record ReauthorizeResult(string AuthorizationId, string Status);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(string RefundId, string Status);

/// <summary>Result of vaulting a card via the Payment Method Tokens API.</summary>
public record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

/// <summary>One transaction as reported by PayPal's Transaction Search API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate);
