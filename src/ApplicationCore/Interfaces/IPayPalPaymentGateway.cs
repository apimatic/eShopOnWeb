using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which the application talks to PayPal. The concrete adapter lives in Infrastructure and
/// is the only place that knows PayPal's REST shapes; everything above this interface deals in domain terms.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order for <paramref name="amount"/> and authorizes it (places a hold) in a single step,
    /// paying either with the supplied one-off <paramref name="card"/> or with a saved card named by
    /// <paramref name="vaultId"/>. <paramref name="idempotencyKey"/> makes a repeat call a no-op at PayPal.
    /// Throws <see cref="Exceptions.PayPalChallengeRequiredException"/> if PayPal demands buyer browser approval.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, string invoiceId, string customId,
        CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the money held by an authorization.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so the funds can still be captured.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Releases an authorization's hold without taking any money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (<paramref name="amount"/> null) or in part, idempotently by key.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card so it can be reused later, returning its token id and a safe description.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Returns PayPal's own record of transactions across the whole [from, to] range (chunked and paged internally).</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
