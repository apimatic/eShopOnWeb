using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, string paymentReference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalPaymentTokenResult> SaveCardAsync(string buyerId, CardInput card, string requestId,
        CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
