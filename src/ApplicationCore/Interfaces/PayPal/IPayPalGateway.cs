using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single seam through which this app talks to PayPal. Every method maps to one PayPal REST call
/// (or, for reconciliation, a paged sequence of calls). Implementations own OAuth, idempotency headers,
/// base-url resolution, and translation of PayPal errors into <see cref="Exceptions.PayPalGatewayException"/>.
/// </summary>
public interface IPayPalGateway
{
    // ----- Orders v2 -----

    /// <summary>Creates a PayPal order with intent=AUTHORIZE for the given amount.</summary>
    Task<PayPalOrderResult> CreateAuthorizationOrderAsync(
        CreateOrderCommand command, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds funds on) an existing PayPal order using the supplied card source.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, PayPalCardPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default);

    // ----- Payments v2 -----

    /// <summary>Captures (settles) an authorization in full, marking it final.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing or past expiry, returning the new authorization.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without taking any money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    // ----- Vault v3 -----

    /// <summary>
    /// Vaults a card and returns its durable token plus a safe description. <paramref name="merchantCustomerId"/>
    /// groups the token under the shopper in PayPal's records (sent as customer.merchant_customer_id).
    /// </summary>
    Task<PayPalVaultCardResult> VaultCardAsync(
        PayPalRawCard card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    // ----- Transaction Search v1 -----

    /// <summary>
    /// Returns every PayPal transaction whose initiation date falls in [from, to], chunking the range
    /// into PayPal's supported windows and paging through all results (not just the first page).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
