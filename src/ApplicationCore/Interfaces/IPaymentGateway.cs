using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). All monetary values are
/// major units (e.g. dollars); the currency code travels with every call.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Puts a hold on the money (intent AUTHORIZE). Either a vaulted card or full card details.</summary>
    Task<AuthorizationResult> AuthorizeAsync(string? vaultTokenId, CardDetails? card, decimal amount, string currency,
        string referenceId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Takes the money held by an authorization (final capture).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Releases a held authorization without taking any money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renews a stale authorization. Throws AuthorizationNotRenewableException when PayPal refuses.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card for a shopper and returns its safe display fields.</summary>
    Task<VaultedCardResult> VaultCardAsync(string buyerId, CardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>PayPal's own record of transactions over a date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
