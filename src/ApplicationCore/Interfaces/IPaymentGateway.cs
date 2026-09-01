using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to the payment provider (PayPal). All types crossing this
/// boundary are provider-agnostic; the implementation owns every SDK detail. Every write
/// takes an idempotency key that the provider uses to dedupe retries.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a provider order for the local order and places an authorization hold for its
    /// full total, either from inline card details or from a vaulted card token.
    /// </summary>
    Task<AuthorizationResult> AuthorizePaymentAsync(AuthorizationRequest request, CancellationToken ct = default);

    /// <summary>Captures an authorization hold — this is when money actually moves.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string? invoiceId, CancellationToken ct = default);

    /// <summary>Reads the current state of an authorization (status, expiry).</summary>
    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization hold (PayPal allows this from day 4 to day 29).</summary>
    Task<AuthorizationInfo> ReauthorizePaymentAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids an authorization hold, releasing the shopper's funds. Returns the final status.</summary>
    Task<string> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a shopper's card for reuse. Only safe descriptors are returned.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? payPalCustomerId, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Deletes a vaulted card at the provider.</summary>
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of transactions over a date range — every page, the whole range.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
