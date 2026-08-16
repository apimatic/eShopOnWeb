using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor. The only implementation talks to PayPal, but the
/// application core depends solely on this neutral contract so the money-movement rules stay free of
/// PayPal-specific detail. All amounts are decimals in the gateway's configured currency.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency the gateway is configured to transact in.</summary>
    string Currency { get; }

    /// <summary>
    /// Authorize (place a hold for) <paramref name="amount"/> using one-off card details. The
    /// <paramref name="idempotencyKey"/> makes a retried authorization safe. Throws
    /// <see cref="Exceptions.PaymentApprovalRequiredException"/> if PayPal demands a browser challenge.
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(string reference, decimal amount,
        CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize a hold using a previously vaulted card token belonging to the shopper.</summary>
    Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string reference, decimal amount,
        string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (settle) an authorization, taking the money.</summary>
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renew a hold that has gone stale, so a fulfilment can still capture it.</summary>
    Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the held funds without charging.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (null amount) or in part.</summary>
    Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card so it can be reused later, returning a token and a safe description.</summary>
    Task<GatewayVaultResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card token so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range (paging and 31-day windows
    /// handled internally), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw card details. Passed straight to the gateway; never persisted or logged by the app.</summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null);

public record GatewayAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand = null,
    string? CardLast4 = null);

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal GrossAmount,
    decimal TotalRefunded);

public record GatewayVaultResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string ExpiryYearMonth,
    string? CardholderName);

/// <summary>A single transaction as PayPal's reporting API reports it.</summary>
public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
