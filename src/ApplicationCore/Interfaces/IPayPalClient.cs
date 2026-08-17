using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault. Held transiently and passed
/// straight to PayPal; never persisted in the application database and never logged.
/// </summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string? BillingCountryCode);

/// <summary>Result of creating a PayPal order and its authorization (the hold).</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastFour,
    string? CardExpiry);

/// <summary>Current state of a PayPal authorization.</summary>
public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of capturing an authorization, including PayPal's fee and net proceeds.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>Result of refunding a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card saved in PayPal's Vault, described safely for the shopper.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFour,
    string Expiry,
    string? CardholderName);

/// <summary>One transaction from PayPal's own record (Transaction Search / reporting).</summary>
public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string EventCode,
    string Status,
    decimal Amount,
    decimal Fee,
    string CurrencyCode,
    DateTimeOffset Date,
    string? InvoiceId);

/// <summary>
/// Thin, typed gateway over the PayPal REST API (Orders v2, Payments v2, Vault v3, Reporting v1).
/// The only place the application talks to PayPal. Handles OAuth token caching and idempotency.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates an order with intent=AUTHORIZE paid by a raw card, and returns the hold.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currencyCode, PayPalCardDetails card, string invoiceReference, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Creates an order with intent=AUTHORIZE paid by a vaulted card, and returns the hold.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string currencyCode, string vaultId, string invoiceReference, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (to detect a stale hold before capture).</summary>
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization (takes the money) and reports the fee and net proceeds.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currencyCode, string invoiceReference, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization; returns the new hold.</summary>
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization (releases the held funds).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full or in part. Idempotent under <paramref name="idempotencyKey"/>.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string invoiceReference, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a raw card and returns its safe descriptor plus the vault token id.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole range (chunked to PayPal's
    /// 31-day window limit and fully paginated), for reconciliation.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
