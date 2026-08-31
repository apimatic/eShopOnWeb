using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level payment processor gateway. The implementation is built to PayPal's
/// OpenAPI specifications (checkout_orders_v2, payments_payment_v2,
/// vault_payment_tokens_v3, transaction_search_v1); every method maps to one
/// operation in those documents.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>POST /v2/checkout/orders with intent=AUTHORIZE and a card payment source.</summary>
    Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(CardDetails card, decimal amount, string currency,
        string requestId, string? customId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/checkout/orders with intent=AUTHORIZE and a vaulted card (vault_id) payment source.</summary>
    Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultTokenId, decimal amount, string currency,
        string requestId, string? customId, CancellationToken cancellationToken = default);

    /// <summary>GET /v2/payments/authorizations/{authorization_id}.</summary>
    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{authorization_id}/capture (final capture of the full amount).</summary>
    Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{authorization_id}/reauthorize.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/authorizations/{authorization_id}/void.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/payments/captures/{capture_id}/refund. Null amount means full remaining refund.</summary>
    Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>POST /v3/vault/payment-tokens (vault a card for a customer).</summary>
    Task<GatewayVaultedCard> SaveCardAsync(string customerId, CardDetails card, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>GET /v3/vault/payment-tokens?customer_id=...</summary>
    Task<IReadOnlyList<GatewayVaultedCard>> ListSavedCardsAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>DELETE /v3/vault/payment-tokens/{id}.</summary>
    Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /v1/reporting/transactions over [from, to], following pagination until
    /// the whole range has been read.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
