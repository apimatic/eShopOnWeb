using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public interface IPayPalClient
{
    Task<PayPalOrderResponse> CreateOrderAsync(string amount, string currency, string customId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalOrderResponse> AuthorizeOrderAsync(string paypalOrderId, PayPalCardSource cardSource, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalGetAuthorizationResponse> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalCaptureResponse> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalReauthorizeResponse> ReauthorizeAsync(string authorizationId, string amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalRefundResponse> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalVaultTokenResponse> CreateVaultPaymentTokenAsync(PayPalVaultCardRequest card, string customerId, string idempotencyKey, CancellationToken ct = default);

    Task<List<PayPalVaultTokenResponse>> ListVaultPaymentTokensAsync(string customerId, CancellationToken ct = default);

    Task DeleteVaultPaymentTokenAsync(string tokenId, CancellationToken ct = default);

    Task<List<PayPalTransactionDetail>> SearchTransactionsAllPagesAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
}
