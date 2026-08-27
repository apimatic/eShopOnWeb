using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. Full card data passes through to the
/// payment provider and is never persisted or logged by this application.
/// </summary>
public sealed record CardPaymentDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    GatewayAddress? BillingAddress);

public sealed record GatewayAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);

public sealed record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayAuthorizationInfo(
    string AuthorizationId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    decimal? Amount,
    string? Currency);

public sealed record GatewaySavedCard(
    string VaultId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public sealed record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    string? InvoiceId);

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations live in Infrastructure.
/// Every write takes an idempotency key which is forwarded to the provider, so a retried
/// request never executes the money movement twice.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorization> AuthorizeCardPaymentAsync(
        decimal amount, string currency, string referenceId, CardPaymentDetails card,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorization> AuthorizeVaultedCardPaymentAsync(
        decimal amount, string currency, string referenceId, string vaultId,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<GatewayAuthorizationInfo> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayRefund> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<GatewaySavedCard> SaveCardAsync(
        string merchantCustomerId, CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
