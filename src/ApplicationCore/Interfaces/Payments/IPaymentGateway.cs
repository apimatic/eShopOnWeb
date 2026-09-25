using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The application's abstraction over PayPal. The implementation is the only place that talks to the
/// PayPal SDK; it translates every SDK failure into <see cref="PaymentGatewayException"/> and never
/// leaks SDK types across this boundary. All amounts are decimals; the implementation formats them for
/// the wire to the cent.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Create a PayPal order (intent=AUTHORIZE) funded by a card and place a hold for the full amount.</summary>
    Task<GatewayAuthorization> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct);

    /// <summary>Re-read the authorization for an order by the PayPal order id (settle path after a broken write).</summary>
    Task<GatewayAuthorization?> TryGetAuthorizationForOrderAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Current status of an authorization by its id.</summary>
    Task<GatewayAuthorizationState?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Capture (take) the full held amount. Populates the fee and net proceeds from PayPal's report.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale hold. Throws <see cref="AuthorizationNotRenewableException"/> when it can no longer be renewed.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Release a hold before capture — no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment, full (amount null) or partial, under a caller idempotency key.</summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, string? note, CancellationToken ct);

    /// <summary>Vault a card for reuse; returns the vault token id and a safe descriptor.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(VaultCardCommand command, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>List PayPal's transaction record over a date range, walking every page.</summary>
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, string currencyCode, CancellationToken ct);
}
