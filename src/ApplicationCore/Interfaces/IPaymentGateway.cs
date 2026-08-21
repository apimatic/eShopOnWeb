using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which the application talks to the payment processor (PayPal). It speaks only
/// domain-shaped models — no processor SDK type crosses this boundary. Every method throws
/// <see cref="Exceptions.PaymentGatewayException"/> with an appropriate HTTP status on failure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a PayPal order (intent=AUTHORIZE) and places the hold for the full amount.</summary>
    Task<GatewayAuthorization> CreateAndAuthorizeAsync(CreateAuthorizationRequest request, CancellationToken ct = default);

    /// <summary>Renews a stale authorization; throws a non-renewable error an operator can act on.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default);

    /// <summary>Captures the authorization at fulfilment and returns gross / fee / net.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids the authorization (releases the held funds).</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a capture in full (<paramref name="amount"/> null) or in part.</summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card and returns a safe description; the PAN is never returned or stored.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>Lists PayPal's own transactions for a date range, paginated over the whole range.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
