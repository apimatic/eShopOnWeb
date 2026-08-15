using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's port to a card payment processor (PayPal). All PayPal-specific detail lives
/// behind this abstraction so the domain and services stay free of the SDK. Amounts are decimal
/// money values; the implementation is responsible for formatting them to the cent.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Places a hold (authorization) for the given amount without capturing it.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously authorized payment (takes the money).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so it can still be captured. Throws
    /// <see cref="PaymentGatewayException"/> (with <see cref="PaymentGatewayException.IsOperatorActionable"/>)
    /// when the authorization can no longer be renewed.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Releases a held authorization (cancel before fulfilment).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part.</summary>
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse and returns a safe descriptor (brand + last 4).</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own record of transactions across the whole date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
