using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Gateway to the PayPal REST API. Every call targets the configured (sandbox) base URL and
/// isolates the HTTP contract from the application's order/payment orchestration.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>The ISO-4217 currency all amounts are expressed in, from configuration.</summary>
    string Currency { get; }

    /// <summary>
    /// Create a PayPal order with intent=AUTHORIZE and process the supplied instrument, placing a hold
    /// equal to the order total. The card is processed at create-time, so the returned result already
    /// carries the authorization.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) the money held by an authorization, at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale hold. Returns the new authorization id/status/expiry.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Release a hold before capture (cancel).</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of an authorization (status + expiry).</summary>
    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, fully or partially. <paramref name="idempotencyKey"/> makes repeats safe.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later reuse (standalone save). Returns the vault token + safe display.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across a date range, following pagination and 31-day
    /// chunking so the whole range is covered.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
