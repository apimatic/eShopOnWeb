using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Hand-written client for the PayPal REST APIs, built against the OpenAPI specifications
/// in api-specs/paypal (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3,
/// transaction_search_v1). requestId maps to the PayPal-Request-Id header and makes every
/// mutating call idempotent at PayPal.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// POST /v2/checkout/orders with intent AUTHORIZE. With a card payment source PayPal
    /// processes the authorization immediately, so the response may already carry it.
    /// </summary>
    Task<PayPalOrderInfo> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/checkout/orders/{id}/authorize. Returns the resulting authorization.</summary>
    Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>GET /v2/payments/authorizations/{id}.</summary>
    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{id}/capture.</summary>
    Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{id}/reauthorize. Renews a stale authorization; PayPal may return a new authorization id.</summary>
    Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{id}/void. Releases the held funds.</summary>
    Task<PayPalAuthorizationInfo> VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/captures/{id}/refund. Amount null means refund in full.</summary>
    Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? customId, string? noteToPayer, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v3/vault/payment-tokens. Vaults a card and returns its safe descriptor.</summary>
    Task<PayPalVaultedCard> CreateVaultPaymentTokenAsync(CardDetails card, string merchantCustomerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>DELETE /v3/vault/payment-tokens/{id}.</summary>
    Task DeleteVaultPaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>GET /v1/reporting/transactions over the whole range (all pages).</summary>
    Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
