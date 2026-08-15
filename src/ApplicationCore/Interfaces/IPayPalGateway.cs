using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, domain-facing abstraction over PayPal. The concrete implementation (Infrastructure) is the
/// only place that references the PayPal SDK; the application layer speaks purely in the
/// <see cref="Payments"/> DTOs below. Every method targets the configured environment/base-url and
/// currency. Implementations translate PayPal errors into the domain exceptions in
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold (authorization) on the money for the given amount WITHOUT capturing it, paying
    /// with either raw card details or a previously-vaulted card. The amount must equal the order
    /// total to the cent. <paramref name="instruction"/> carries a stable idempotency key so a
    /// repeated call does not create a second hold.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures (takes the money for) an existing authorization. Returns PayPal's reported captured
    /// amount, fee and net proceeds. Throws <see cref="AuthorizationNotCapturableException"/> when the
    /// authorization can no longer be captured (e.g. it has gone stale) so the caller can reauthorize.
    /// </summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string currencyCode, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a stale authorization, producing a fresh hold for the same amount. Throws
    /// <see cref="AuthorizationNotReauthorizableException"/> (with an operator-actionable message) when
    /// the authorization can no longer be renewed.
    /// </summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases an authorization's held funds (cancel before capture). Idempotent under the same key.</summary>
    Task<VoidResult> VoidAsync(string authorizationId, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, in full (<paramref name="amount"/> null) or in part. The caller-supplied
    /// <paramref name="idempotencyKey"/> is passed to PayPal so a repeated request under the same key
    /// does not refund twice; two distinct keys yield two distinct partial refunds.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card and returns its vault id plus a safe display descriptor (brand + last digits +
    /// expiry). No full card number ever leaves this call.
    /// </summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions between two instants, paging over the WHOLE range
    /// (not just the first page). Returns an empty list when PayPal has no data for the range yet
    /// (reporting lag is expected in sandbox).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
