using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the PayPal REST API. Implementations hide the concrete HTTP
/// call sequencing (create-order + authorize, capture, reauthorize, void, refund,
/// vault, transaction search) and surface only the state eShop needs to persist and act on.
/// All monetary values are handled to the cent.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured ISO-4217 currency all amounts are denominated in.</summary>
    string Currency { get; }

    /// <summary>Creates a PayPal order for the amount and places an authorization hold
    /// using raw card details (card-not-present). Returns the PayPal order id and the
    /// resulting authorization. <paramref name="requestId"/> makes the call idempotent.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(PayPalMoney amount, CardDetails card, string requestId, CancellationToken ct = default);

    /// <summary>As <see cref="AuthorizeWithCardAsync"/> but pays with a previously vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(PayPalMoney amount, string vaultId, string requestId, CancellationToken ct = default);

    /// <summary>Captures (takes the money for) an existing authorization.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, PayPalMoney amount, string requestId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization, returning a fresh authorization to capture against.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, PayPalMoney amount, string requestId, CancellationToken ct = default);

    /// <summary>Reads the current state of an authorization.</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a capture in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, PayPalMoney? amount, string requestId, CancellationToken ct = default);

    /// <summary>Vaults (saves) a card for later reuse, associated with a PayPal customer id.
    /// Returns the vault token and safe display details — never card data.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string requestId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>PayPal's own record of transactions across the full date range
    /// (paging through every page), for reconciliation.</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
