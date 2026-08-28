using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<string> CreateOrderAsync(
        int orderId,
        decimal amount,
        string currency,
        string invoiceId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId,
        int orderId,
        decimal amount,
        string currency,
        CardInput? card,
        string? vaultId,
        int authorizationAttempt,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string payPalOrderId,
        decimal amount,
        string currency,
        DateTimeOffset originalExpirationTime,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<string> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalSavedCardResult> SaveCardAsync(
        CardInput card,
        string merchantCustomerId,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<PayPalTransactionPage> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        CancellationToken cancellationToken);
}
