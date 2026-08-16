using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These flow straight through to PayPal and
/// are never persisted in this app's database nor written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string PostalCode,
    string CountryCode);

/// <summary>Result of authorizing (placing a hold on) an order's total with PayPal.</summary>
public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4,
    string? VaultTokenId = null,
    string? VaultCustomerId = null);

/// <summary>Result of capturing a previously authorized amount, with PayPal's fee breakdown.</summary>
public record CaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>Result of renewing (reauthorizing) a stale hold.</summary>
public record ReauthorizeResult(
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting (saving) a card for later reuse.</summary>
public record VaultResult(
    string TokenId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry);

/// <summary>A single transaction as PayPal's transaction-search reporting knows it.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset Date,
    string? EventCode,
    string? InvoiceId,
    string? CustomField,
    string? PayPalReferenceId);

/// <summary>
/// The one and only way this app talks to PayPal. Every method maps to a documented PayPal REST v2/v3
/// operation. Implementations own OAuth token acquisition/caching, idempotency headers, and error
/// translation into <see cref="Exceptions.PayPalApiException"/>.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create an AUTHORIZE-intent order and authorize it inline with a one-off card. Optionally vault the card.</summary>
    Task<AuthorizeResult> AuthorizeWithCardAsync(decimal amount, string currency, string orderReference,
        CardDetails card, bool storeInVault, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Create an AUTHORIZE-intent order and authorize it inline with a saved (vaulted) card.</summary>
    Task<AuthorizeResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string orderReference,
        string vaultTokenId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Capture an authorization (take the held money). Returns the fee/net breakdown PayPal reports.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so the money can still be taken at fulfilment.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Release a hold (cancel before fulfilment). Idempotent: voiding an already-voided hold succeeds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, fully or partially. The idempotency key doubles as PayPal-Request-Id.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card for later reuse. Returns the token id and a safe descriptor.</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List every transaction PayPal recorded across a date range, following pagination and chunking
    /// the range into windows PayPal's reporting accepts, so the whole range is covered.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
