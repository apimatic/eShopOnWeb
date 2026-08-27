using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations are built against the
/// provider's OpenAPI contract in api-specs/.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a provider order with intent AUTHORIZE for the given amount.</summary>
    Task<GatewayOrder> CreateOrderAsync(decimal amount, string currencyCode, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the funds on a previously created provider order.</summary>
    Task<GatewayAuthorization> AuthorizeOrderAsync(string gatewayOrderId, GatewayPaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization.</summary>
    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) previously authorized funds.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for the given merchant-side customer id.</summary>
    Task<GatewaySavedCard> SaveCardAsync(GatewayCard card, string customerId, CancellationToken cancellationToken = default);

    /// <summary>Lists the vaulted cards for the given merchant-side customer id.</summary>
    Task<IReadOnlyList<GatewaySavedCard>> ListSavedCardsAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card.</summary>
    Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of the provider's own transaction report for a date range.</summary>
    Task<GatewayTransactionPage> GetTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken = default);
}
