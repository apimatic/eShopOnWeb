using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public record CardAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record SaveCardResult(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? FeeAmount,
    decimal? NetAmount);

public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record TransactionRecord(
    string TransactionId,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiatedAt);
