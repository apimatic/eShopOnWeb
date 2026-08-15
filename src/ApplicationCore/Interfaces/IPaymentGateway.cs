using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external payment processor (PayPal). The application/domain layer
/// depends only on this contract; the concrete implementation lives in Infrastructure and is the
/// sole place the PayPal SDK is used. All money amounts are decimal to the cent; currency is an
/// ISO-4217 code supplied by the caller (bound from configuration).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorize (place a hold for) an amount using raw card details for a one-off payment.
    /// Does not capture. Throws <see cref="PaymentChallengeRequiredException"/> if the processor
    /// requires shopper browser approval (3DS) instead of authorizing directly.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize (place a hold for) an amount using a previously vaulted card token.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) a previously placed authorization hold.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renew a stale/expired authorization so it can be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Void (release) an authorization hold before capture.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Store a card in the vault and return a reusable token plus a safe description.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Remove a card from the vault. Best-effort; safe to call for an already-removed token.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the processor's own record of transactions for a date range, covering the whole range
    /// (all pages), for reconciliation against local orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw card details for a one-off payment or to vault. Never stored or logged by this app.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record RefundResult(string RefundId, string Status);

public record VaultedCard(
    string VaultId,
    string CardBrand,
    string Last4,
    string ExpiryMonth,
    string ExpiryYear,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate);
