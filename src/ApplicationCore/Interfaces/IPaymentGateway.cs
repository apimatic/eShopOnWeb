using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. Implemented in Infrastructure so the PayPal SDK
/// dependency stays out of ApplicationCore and PublicApi. Every method converts SDK and transport
/// failures into <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The configured currency (from PayPal:Currency) that amounts are charged in.</summary>
    string CurrencyCode { get; }

    /// <summary>Authorizes (holds) the order total. The held amount equals the request amount to the cent.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct = default);

    /// <summary>Reads the current state of an authorization (status / expiry).</summary>
    Task<AuthorizationView> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization, returning the (possibly new) authorization id.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default);

    /// <summary>Captures (takes) the money for an authorization at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids an authorization before fulfilment, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, in full (null amount) or in part, keyed by the caller's idempotency key.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults (saves) a card, returning its vault id and safe display fields.</summary>
    Task<SavedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range, across ALL pages, for reconciliation
    /// against eShop orders.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
