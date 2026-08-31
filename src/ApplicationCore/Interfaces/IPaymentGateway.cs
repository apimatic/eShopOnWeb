using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used for a one-off payment or for vaulting. The full PAN and security
/// code flow through this abstraction in memory only - they are never persisted or logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? HolderName,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public sealed record GatewayAuthorization(
    string GatewayOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record GatewayAuthorizationDetails(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? GatewayFee,
    decimal? NetAmount,
    string Currency);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? TotalRefundedAmount);

public sealed record GatewayVaultedCard(
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
    string? Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomId,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations translate these
/// operations to processor API calls; callers stay processor-agnostic.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorization> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<GatewayAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? note, CancellationToken cancellationToken = default);
    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
