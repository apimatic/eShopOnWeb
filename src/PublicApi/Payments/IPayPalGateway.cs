using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalGateway
{
    Task<ProviderAuthorization> AuthorizeAsync(int orderId, decimal amount, string currency,
        ProviderCardSource source, string createRequestId, string authorizeRequestId, CancellationToken ct);
    Task<ProviderAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<ProviderAuthorizationStatus> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct);
    Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct);
    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken ct);
    Task<ProviderAuthorizationStatus> VoidAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<ProviderRefund> RefundAsync(string captureId, decimal amount, string currency,
        bool fullRemainingRefund, string requestId, CancellationToken ct);
    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken ct);
    Task<ProviderPaymentMethod> SaveCardAsync(string shopperId, CardInput card, string requestId, CancellationToken ct);
    Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId, CancellationToken ct);
    Task DeleteCardAsync(string tokenId, CancellationToken ct);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}
