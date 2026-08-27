using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. Used only in flight — never persisted
/// and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddressDetails? BillingAddress);

public record BillingAddressDetails(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationStatus(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? Fee,
    decimal? Net);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount);

public record GatewayVaultedCard(
    string TokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Abstraction over the payment provider (PayPal). All write operations accept an idempotency
/// key that is forwarded to the provider so a repeated call never executes twice.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> for an order. Exactly one of
    /// <paramref name="card"/> / <paramref name="vaultTokenId"/> must be supplied.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken ct);

    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>
    /// Renews a stale authorization. Throws <see cref="Exceptions.PaymentGatewayException"/>
    /// with a 422 provider status when the authorization can no longer be renewed.
    /// </summary>
    Task<GatewayAuthorizationStatus> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Captures the full authorized amount.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Releases a held authorization without moving money.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a capture; <paramref name="amount"/> null means the full captured amount.</summary>
    Task<GatewayRefund> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>
    /// Vaults a card. <paramref name="customerId"/> is the provider-generated customer id from a
    /// previous vault for the same shopper (null on the shopper's first card);
    /// <paramref name="merchantCustomerId"/> is our own shopper key. The result carries the
    /// provider-generated customer id to persist.
    /// </summary>
    Task<GatewayVaultedCard> VaultCardAsync(
        string? customerId,
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    /// <summary>Returns every transaction the provider recorded in [from, to], all pages.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
