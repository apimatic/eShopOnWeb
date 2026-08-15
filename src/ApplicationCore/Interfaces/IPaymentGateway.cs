using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Keeps the SDK isolated in Infrastructure so the
/// application layer deals only in domain terms. All money movement flows through this seam.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Create a hold (authorization) for the amount, paying with a one-off raw card.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Create a hold (authorization) for the amount, paying with a previously vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) a previously created authorization.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so it can still be captured. Returns the new authorization.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full or in part. The idempotency key makes a repeat a no-op.</summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a raw card for later reuse. Returns the vault token and a safe descriptor.</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own transaction records over a date range, following pagination so the whole
    /// range is returned rather than the first page only.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
