using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Thin gateway over the PayPal REST API (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// The only place in the app that talks to PayPal.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Hold the amount against a one-off card (intent=AUTHORIZE). Returns the created authorization.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardPaymentDetails card,
        string idempotencyKey, string? customId, string? invoiceId, CancellationToken ct);

    /// <summary>Hold the amount against a previously vaulted card (by vault token id).</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, string? customId, string? invoiceId, CancellationToken ct);

    /// <summary>Vault a card for reuse. Returns the permanent payment-token id and a safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct);

    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Take the money for a held authorization (at fulfilment).</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale authorization so fulfilment need not fail outright.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct);

    /// <summary>Release a held authorization (at cancel), so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refund a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? note, CancellationToken ct);

    /// <summary>PayPal's own transaction record for a date range — every page, chunked across the 31-day API limit.</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>
/// Raised when PayPal rejects a request in a way an operator or shopper can act on
/// (declined card, an authorization that cannot be renewed, a refund that would exceed the capture).
/// </summary>
public class PayPalException : Exception, IApiException
{
    /// <summary>Suggested HTTP status for the API surface (e.g. 422 for a declined instrument).</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's debug_id when available, for support correlation.</summary>
    public string? DebugId { get; }

    public PayPalException(string message, int statusCode = 422, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }
}
