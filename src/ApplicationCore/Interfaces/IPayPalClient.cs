using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A typed client over the PayPal REST APIs described by the OpenAPI specs in <c>api-specs/</c>.
/// The implementation owns HTTP, OAuth token management and (de)serialization; callers work in
/// domain terms. Every method throws <see cref="Exceptions.PayPalApiException"/> on a PayPal error.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Authorizes (holds) an amount by creating a v2 checkout order with intent=AUTHORIZE and a
    /// one-off card. <paramref name="requestId"/> is sent as PayPal-Request-Id for idempotency.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        string invoiceId, PayPalCardInput card, string requestId, CancellationToken ct = default);

    /// <summary>Authorizes (holds) an amount using a previously vaulted card token.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency,
        string invoiceId, string vaultId, string requestId, CancellationToken ct = default);

    /// <summary>Captures (takes) a previously authorized hold.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken ct = default);

    /// <summary>Reads a capture, whose GET response carries the settled fee/net breakdown.</summary>
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct = default);

    /// <summary>Reads the current status of an authorization (e.g. CREATED, EXPIRED, VOIDED).</summary>
    Task<string?> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renews a stale hold, producing a fresh authorization for the same amount.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct = default);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Refunds a capture in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken ct = default);

    /// <summary>Vaults a card, returning the token id and a safe description of the card.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardInput card, string? customerId,
        string requestId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own transaction records over a date range, following pagination so the
    /// whole range is covered rather than just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default);
}
