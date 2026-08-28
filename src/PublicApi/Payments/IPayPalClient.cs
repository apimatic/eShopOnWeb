using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, string invoiceId, string referenceId,
        decimal amount, string currency, IReadOnlyCollection<PayPalOrderItem> items,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string invoiceId, string referenceId, string? note, string requestId,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCardResult> CreatePaymentTokenAsync(string merchantCustomerId, PayPalCard card,
        string requestId, CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);

    Task<PayPalTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken);
}
