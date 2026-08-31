using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(string reference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(string orderId, CardInput card,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(string orderId, string vaultId,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string customId, string? note, string requestId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(CardInput card, string merchantCustomerId,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken);
}
