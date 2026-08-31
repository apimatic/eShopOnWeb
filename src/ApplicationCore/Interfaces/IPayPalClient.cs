using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs used by this application:
/// Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Vault v3 (saved cards) and Transaction Search v1 (reconciliation).
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE for the given amount.</summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes a PayPal order with a one-off card. Throws
    /// <see cref="Exceptions.PaymentActionRequiredException"/> if PayPal demands
    /// a browser-based shopper challenge.
    /// </summary>
    Task<PayPalAuthorization> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes a PayPal order with a previously vaulted card.</summary>
    Task<PayPalAuthorization> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (status, expiry).</summary>
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization in full (final capture).</summary>
    Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews an authorization whose honor period has expired.</summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (amount null) or partially.</summary>
    Task<PayPalRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns its safe display representation.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions in [from, to], paging through
    /// the whole range. Range must not exceed 31 days.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
