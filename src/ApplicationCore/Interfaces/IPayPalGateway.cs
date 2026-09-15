using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<AuthorizationResult> AuthorizeAsync(string payPalOrderId, PaymentSource source,
        string requestId, CancellationToken cancellationToken);
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<AuthorizationResult> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardDetails card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
