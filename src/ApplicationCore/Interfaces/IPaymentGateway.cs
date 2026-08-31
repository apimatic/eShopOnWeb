using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment processor gateway. All money movement and card vaulting goes through this
/// abstraction; the implementation talks to PayPal. Every mutating operation accepts an
/// idempotency key which is forwarded to the processor so retries never double-charge.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency all payments are made in (from configuration).</summary>
    string Currency { get; }

    Task<PayPalOrderCreated> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, GatewayPaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(GatewayCardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset start, DateTimeOffset end, int page, int pageSize, CancellationToken cancellationToken = default);
}
