using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. Implemented in the Infrastructure layer over the PayPal
/// Server SDK; ApplicationCore depends only on this interface and the plain DTOs in
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/>, never on SDK types.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency all amounts are expressed in (from configuration).</summary>
    string Currency { get; }

    /// <summary>
    /// Authorize (place a hold on) <paramref name="amount"/> using either one-off <paramref name="card"/>
    /// details or a saved-card <paramref name="vaultId"/> (exactly one is supplied). Does not capture.
    /// </summary>
    Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, string invoiceReference,
        CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale authorization; returns the new authorization (a fresh id/expiry).</summary>
    Task<PaymentAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Read the current status/expiry of an authorization.</summary>
    Task<PaymentAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Capture (take) the full authorized amount; reports what PayPal captured, its fee, and the net.</summary>
    Task<PaymentCaptureResult> CaptureAsync(string authorizationId, string invoiceReference,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Void an authorization before capture, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<PaymentRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken ct);

    /// <summary>Vault a card for reuse; returns the vault token id and safe display details.</summary>
    Task<SavedCardResult> VaultCardAsync(string customerReference, CardDetails card, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>PayPal's own record of transactions across the whole date range (all windows and pages).</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}
