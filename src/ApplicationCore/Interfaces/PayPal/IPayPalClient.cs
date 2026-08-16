using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Port over the subset of PayPal's REST API this integration uses. The implementation talks to the
/// PayPal Orders v2, Payments v2, Vault v3 and Transaction Search APIs directly.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates an order with intent=AUTHORIZE paid by a one-off card, holding the total. Idempotent on <paramref name="requestId"/>.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Creates an order with intent=AUTHORIZE paid by a vaulted card, holding the total. Idempotent on <paramref name="requestId"/>.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization.</summary>
    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so it can still be captured. Throws <see cref="PayPalApiException"/> when it can no longer be renewed.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) an authorized amount. Idempotent on <paramref name="requestId"/>.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string invoiceId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the hold. Safe to call more than once.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture in full or in part. Idempotent on <paramref name="requestId"/>.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning the token and safe descriptors. Optionally reuses an existing customer id.</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Lists every PayPal transaction in the range, paging across the whole range (chunked to PayPal's 31-day window).</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when PayPal returns an error, carrying the detail an operator needs to act.</summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? Issue { get; }
    public string? DebugId { get; }

    public PayPalApiException(int statusCode, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }
}
