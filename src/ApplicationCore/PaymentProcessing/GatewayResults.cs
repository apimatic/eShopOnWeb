using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresOn);

public record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresOn);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultTokenResult(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record TransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    string? InitiationDate);

public record TransactionSearchResult(
    IReadOnlyList<TransactionRecord> Transactions,
    IReadOnlyList<string> Warnings);
