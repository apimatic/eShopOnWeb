using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public interface IPayPalClient
{
    Task<PayPalOrderResponse> CreateOrderAsync(int orderId, string merchantReference, decimal amount, string currency,
        CancellationToken cancellationToken);
    Task<PayPalOrderResponse> AuthorizeOrderAsync(string payPalOrderId, PayPalCard card,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, string merchantReference, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, string merchantReference, decimal amount, string currency,
        string idempotencyKey, string? note, CancellationToken cancellationToken);
    Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(string buyerId, string? payPalCustomerId,
        PayPalCard card, string idempotencyKey, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransactionDetail>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
