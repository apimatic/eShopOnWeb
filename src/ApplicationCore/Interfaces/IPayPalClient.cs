using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// A thin, task-shaped wrapper over the PayPal Orders v2, Payments v2, Vault v3 and
// Transaction Search v1 REST APIs. All PayPal wire-format detail (snake_case JSON, HATEOAS
// links, OAuth token handling) is confined to the Infrastructure-side implementation.
public interface IPayPalClient
{
    // Creates a PayPal order (intent=AUTHORIZE) for the given amount, then immediately
    // authorizes it against the supplied payment source (a one-off card, or a vaulted
    // card id) so no buyer approval redirect is required.
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(decimal amount, string currency, PayPalPaymentSource paymentSource, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default);

    Task DeleteVaultedPaymentTokenAsync(string paymentTokenId, CancellationToken ct = default);

    // Pages and date-chunks internally so the caller always gets every transaction in the
    // requested range, not just the first page/window.
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
