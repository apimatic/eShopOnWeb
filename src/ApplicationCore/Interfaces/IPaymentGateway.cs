using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations must honor the
/// idempotency keys so a retried call never authorizes, captures or refunds twice.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorizationResult> AuthorizeCardAsync(CardDetails card, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationResult> AuthorizeVaultedCardAsync(string vaultTokenId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    Task<GatewayVaultTokenResult> CreateVaultTokenAsync(CardDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record GatewayAuthorizationResult(
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

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record GatewayVaultTokenResult(
    string VaultTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? Time);
