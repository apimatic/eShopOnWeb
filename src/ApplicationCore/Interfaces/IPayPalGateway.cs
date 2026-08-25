using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the PayPal SDK for the direct-card, vault and reporting operations this
/// application needs. Implemented in Infrastructure so ApplicationCore never references the SDK.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes (holds, does not capture) <paramref name="amount"/> against either raw
    /// <paramref name="card"/> details or a previously vaulted <paramref name="vaultId"/> — exactly
    /// one of the two must be supplied. <paramref name="requestId"/> is sent as PayPal's
    /// idempotency header; a repeated call with the same value returns the original authorization.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(string requestId, decimal amount, string currency, CardDetails? card,
        string? vaultId, CancellationToken ct);

    /// <summary>Renews an authorization that has passed its 3-day honor period (valid up to 29 days total).</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string requestId, string authorizationId, decimal amount,
        string currency, CancellationToken ct);

    /// <summary>Captures a held authorization in full — the point at which funds actually move.</summary>
    Task<CaptureResult> CaptureAsync(string requestId, string authorizationId, CancellationToken ct);

    /// <summary>Releases a held authorization; no funds ever move.</summary>
    Task VoidAsync(string requestId, string authorizationId, CancellationToken ct);

    /// <summary>
    /// Refunds a captured payment, in full or in part. <paramref name="idempotencyKey"/> is the
    /// caller-supplied key sent as PayPal's idempotency header.
    /// </summary>
    Task<RefundResult> RefundAsync(string idempotencyKey, string captureId, decimal amount, string currency,
        CancellationToken ct);

    /// <summary>Vaults a raw card for later reuse. Only the returned token id and descriptors are safe to store.</summary>
    Task<VaultTokenResult> CreateVaultTokenAsync(string requestId, CardDetails card, CancellationToken ct);

    /// <summary>Detaches a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own transaction records for the given date range, walking every page and
    /// chunking the range as needed. The range is inclusive of both endpoints.
    /// </summary>
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
