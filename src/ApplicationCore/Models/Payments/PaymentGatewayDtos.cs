using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, used only in transit to the payment gateway. Never persisted, never logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public sealed record AuthorizationRequest(
    decimal Amount,
    string Currency,
    string CustomId,
    string InvoiceId,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultTokenId);

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record AuthorizationDetails(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record VaultedCardResult(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? TransactionTime,
    string? InvoiceId,
    string? CustomId);
