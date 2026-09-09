using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic gateway for taking card payments and vaulting cards. The only implementation
/// talks to PayPal, but the domain and services depend on this abstraction, not on PayPal types.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The three-letter currency all amounts are denominated in, from configuration.</summary>
    string Currency { get; }

    /// <summary>
    /// Creates an order with the gateway and places a hold (authorization) on the money for the
    /// full amount, using either a one-off card or a previously vaulted card. Does not capture.
    /// </summary>
    Task<AuthorizationResult> CreateAuthorizedOrderAsync(CreateAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current status/expiry of a hold, used to detect a stale authorization.</summary>
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing/!past expiry, returning the refreshed authorization.</summary>
    Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the held funds. The result carries the fee/net breakdown PayPal reports.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without taking any money.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (null amount) or partially, keyed for idempotency.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns the durable token plus safe display details.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the gateway's own record of transactions across the whole date range (all pages),
    /// for lining up against eShop orders during reconciliation.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
