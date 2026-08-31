using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string Currency);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? Type,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? PayPalReferenceId,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);
