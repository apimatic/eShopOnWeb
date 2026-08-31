using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal, per the OpenAPI specs in api-specs/).
/// Implementations must never log or persist full card details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a processor order with AUTHORIZE intent and authorizes the given amount,
    /// either with one-off card details or with a vaulted card token.
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string referenceId,
        GatewayCardDetails? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<GatewayCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationStatus> ReauthorizeAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        string? customId,
        string? noteToPayer,
        CancellationToken cancellationToken = default);

    Task<GatewayVaultedCard> CreatePaymentTokenAsync(
        string customerId,
        GatewayCardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the processor's own record of transactions over the whole range, following
    /// pagination until every page has been read.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record GatewayCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    GatewayAddress? BillingAddress);

public record GatewayAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record GatewayAuthorizationResult(
    string ProcessorOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationStatus(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string Currency);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record GatewayVaultedCard(
    string VaultTokenId,
    string? CardholderName,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
