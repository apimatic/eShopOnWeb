using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, CardDetails? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string customId, string requestId, CancellationToken cancellationToken);
    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string merchantCustomerId,
        CardDetails card, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken);
}
