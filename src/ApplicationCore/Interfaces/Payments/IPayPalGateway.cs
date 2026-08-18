using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Thin abstraction over PayPal. Every method wraps a single PayPal capability; all provider/transport/parse
/// failures are surfaced as <see cref="Exceptions.PaymentGatewayException"/> (or the
/// <see cref="Exceptions.PaymentReauthorizationException"/> specialisation) so callers see one failure type.
/// This is the seam faked in tests.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Authorize (hold) <paramref name="amount"/> using raw card details. Money is NOT captured.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Authorize (hold) <paramref name="amount"/> using a previously vaulted card token.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Read the current state of an authorization (to detect staleness before capture).</summary>
    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>
    /// Renew a stale authorization. Throws <see cref="Exceptions.PaymentReauthorizationException"/> when the
    /// authorization can no longer be reauthorized.
    /// </summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct);

    /// <summary>Capture an authorization. Returns the captured amount, PayPal's fee, and the net proceeds.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Void an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refund a capture, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken ct);

    /// <summary>Vault a raw card for later reuse, without charging it.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerReference, CancellationToken ct);

    /// <summary>Delete a vaulted card token.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// List PayPal's own transaction records for a date range, covering the whole range across all pages.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(System.DateTimeOffset from,
        System.DateTimeOffset to, CancellationToken ct);
}
