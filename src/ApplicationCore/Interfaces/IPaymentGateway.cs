using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic payment gateway. The PayPal implementation converts every provider
/// failure into PaymentGatewayException, so callers only ever see one failure type.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a provider order for the exact amount and authorizes it (hold, no capture).</summary>
    Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Authorizes an existing provider order (created by an earlier interrupted attempt) —
    /// the replay path that never creates a second hold.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeExistingOrderAsync(string providerOrderId, GatewayAuthorizeSource source, CancellationToken ct = default);

    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Captures (takes) the authorized amount; returns gross/fee/net.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Voids an authorization: held funds are released, no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a capture for the given amount (full or partial).</summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency, string? invoiceReference, CancellationToken ct = default);

    Task<GatewayRefund> GetRefundAsync(string refundId, CancellationToken ct = default);

    /// <summary>Re-reads a capture (e.g. to pick up fee/net once a pending capture settles).</summary>
    Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken ct = default);

    /// <summary>Re-reads provider state for a checkout order (used to settle unknown outcomes and list refunds).</summary>
    Task<GatewayOrderSnapshot> GetOrderSnapshotAsync(string providerOrderId, CancellationToken ct = default);

    /// <summary>Stores a card in the provider vault under the merchant-side customer id; returns token + display fields.</summary>
    Task<SavedVaultCard> VaultCardAsync(string merchantCustomerId, CardCredential card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedVaultCard>> ListVaultCardsAsync(string vaultCustomerId, CancellationToken ct = default);

    Task DeleteVaultCardAsync(string tokenId, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of transactions for [from, to] — whole range, all pages
    /// (the implementation splits long ranges into provider-supported windows).
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
