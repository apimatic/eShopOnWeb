using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalGateway
{
    Task<AuthorizationResult> AuthorizeAsync(int localOrderId, decimal amount, CardInput? card, string? vaultId, string createRequestId, string authorizeRequestId, CancellationToken cancellationToken);
    Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<RefundProviderResult> RefundAsync(string captureId, decimal amount, bool fullRemainder, string requestId, CancellationToken cancellationToken);
    Task<RefundProviderResult> GetRefundAsync(string refundId, decimal expectedAmount, CancellationToken cancellationToken);
    Task<SavedCardProviderResult> SaveCardAsync(string buyerId, CardInput card, string setupRequestId, string tokenRequestId, CancellationToken cancellationToken);
    Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
