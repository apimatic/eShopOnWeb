using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. The only seam that talks to PayPal; the
/// implementation (Infrastructure) wraps the PayPal Server SDK, owns the configured currency, and
/// translates provider failures into <see cref="Exceptions.PaymentException"/>. Idempotency keys are
/// supplied by callers (derived deterministically) and sent to PayPal as its request id so a resend
/// dedupes provider-side.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency (from configuration) every amount is denominated in.</summary>
    string CurrencyCode { get; }

    /// <summary>Authorize (place a hold for) the amount. Does not capture.</summary>
    Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken);

    /// <summary>Re-read the current authorization state (staleness / unknown-outcome settlement).</summary>
    Task<AuthorizationLookup> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Capture (take) an authorized payment, in full when <paramref name="amount"/> is null.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken);

    /// <summary>Renew a stale authorization before capture.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey, decimal amount, CancellationToken cancellationToken);

    /// <summary>Release a held (uncaptured) authorization.</summary>
    Task<VoidResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment, in full when <paramref name="amount"/> is null.</summary>
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken);

    /// <summary>Vault a card and return its vault id plus a safe descriptor.</summary>
    Task<VaultResult> VaultCardAsync(VaultCardRequest request, string idempotencyKeyBase, CancellationToken cancellationToken);

    /// <summary>Revoke a vaulted card at PayPal.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>PayPal's own transaction records for a date range, walking every page.</summary>
    Task<ReconciliationTransactions> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
