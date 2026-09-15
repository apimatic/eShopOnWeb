using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalOrderResult> CreateOrderAsync(string paymentReference, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalCard card, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, string vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string requestId, CancellationToken cancellationToken);
    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string customerId, PayPalCard card, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
