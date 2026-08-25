using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

public record GatewayAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public record GatewayReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset ExpiresAt);

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFeeAmount,
    decimal? NetAmount,
    string Currency,
    DateTimeOffset CapturedAt);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record GatewaySavedCardResult(
    string VaultId,
    string Brand,
    string Last4,
    string ExpiryYearMonth);

public record GatewayTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType,
    string Status,
    string EventCode,
    decimal Amount,
    string Currency,
    DateTimeOffset InitiatedAt,
    DateTimeOffset UpdatedAt);
