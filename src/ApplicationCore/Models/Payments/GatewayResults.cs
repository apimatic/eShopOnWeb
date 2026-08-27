using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationInfo(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record GatewayVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    DateTimeOffset? Time,
    string? InvoiceId,
    string? CustomField);
