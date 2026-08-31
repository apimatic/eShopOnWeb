using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalAuthorization> AuthorizeAsync(string externalReference, int authorizationAttempt, decimal amount, string currency, PayPalCard? card, string? vaultId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string externalReference, string authorizationId, decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string externalReference, string authorizationId, decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string externalReference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string requestId, string captureId, decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayPalSavedCard> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken);
}
