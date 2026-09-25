using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. All PayPal interaction happens behind this seam;
/// no SDK type leaks past it. Implementations translate provider failures into
/// <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorize (place a hold for) the order total using a one-off card or a saved card.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken cancellationToken);

    /// <summary>Read the current state of an authorization (status, expiry).</summary>
    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Renew a stale authorization so fulfilment can still capture it.</summary>
    Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Capture (take) the money for an authorization at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Void an authorization (release the held funds) — used to cancel before fulfilment.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Vault (save) a card for later reuse.</summary>
    Task<VaultCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken cancellationToken);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>PayPal's own record of transactions for a date range, covering the whole range.</summary>
    Task<ReconciliationReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
