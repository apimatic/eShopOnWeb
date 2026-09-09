using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These never touch this application's own
/// database or logs — they are handed straight to PayPal.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PayPalReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string? Name);

public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal Amount,
    string Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    string? EventCode);

/// <summary>
/// Abstraction over the subset of the PayPal REST API this integration uses. The implementation
/// owns credential/token handling, idempotency headers and JSON shapes; the application services
/// stay free of transport concerns.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Currency all amounts are denominated in (from PayPal:Currency configuration).</summary>
    string Currency { get; }

    /// <summary>
    /// Create a PayPal order with AUTHORIZE intent for the given amount and place a hold on the funds
    /// using either raw card details or a saved card (vault id). Returns the resulting authorization.
    /// Throws <see cref="Exceptions.PaymentChallengeException"/> if PayPal requires browser approval.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string reference, decimal amount,
        CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture (take) an authorized payment. This is the money actually moving.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renew a stale authorization, producing a fresh authorization id.</summary>
    Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        CancellationToken ct = default);

    /// <summary>Void an authorization, releasing the held funds (no money moves).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refund a capture in full (null amount) or in part. Idempotent on the given key.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault a card (setup token then payment token) so it can be reused. No browser step.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range, chunked into ≤31-day windows
    /// and fully paginated so nothing beyond the first page is missed.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default);
}
