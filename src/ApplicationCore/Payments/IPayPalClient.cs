using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderCreationResult> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalPaymentSource paymentSource,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult?> GetOrderAuthorizationAsync(string paypalOrderId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<PayPalVoidResult> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken);

    Task<PayPalSavedCardResult> SaveCardAsync(PayPalCardDetails card, string? customerId,
        string setupRequestId, string tokenRequestId, CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
