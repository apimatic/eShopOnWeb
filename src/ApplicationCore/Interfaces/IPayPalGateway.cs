using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A domain-facing boundary over PayPal. Implementations translate to/from the PayPal SDK; no SDK
/// types cross this interface. All methods throw
/// <see cref="Exceptions.PaymentException"/> on failure.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured settlement currency (PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>
    /// Places a hold for <paramref name="amount"/> using the supplied instrument (a raw card or a
    /// vaulted card token). Does not capture. <paramref name="idempotencyKey"/> makes a double-click safe.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Captures (takes the money for) a previously placed hold.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renews a stale hold so it can be captured. Throws AuthorizationNotRenewable if it cannot.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct = default);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment. Pass <paramref name="amount"/> for a partial refund. The
    /// <paramref name="idempotencyKey"/> ensures a repeated request never refunds twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card for later reuse, returning a stable token id and a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer fund an order.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own transaction records for a date range, covering the whole range via pagination.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
