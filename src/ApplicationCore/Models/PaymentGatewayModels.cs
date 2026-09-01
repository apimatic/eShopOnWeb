using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Full card details, used only on the way IN to the payment provider. Never persisted,
/// never logged. Expiry is "YYYY-MM".
/// </summary>
public sealed record CardPaymentDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    string BillingCountryCode);

public sealed record GatewayAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayAuthorizationStatus(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt,
    decimal? Amount,
    string? Currency);

public sealed record GatewayCaptureResult(
    string CaptureId,
    string? Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string? Currency);

public sealed record GatewayRefundResult(
    string RefundId,
    string? Status,
    decimal Amount,
    string? Currency);

public sealed record GatewayVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public sealed record GatewayTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    string? InvoiceId,
    string? CustomField,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType);
