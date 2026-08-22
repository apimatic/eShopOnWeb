using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    string Currency { get; }

    Task<AuthorizePaymentResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken);
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<ReauthorizePaymentResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken);
    Task<CapturePaymentResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<CapturePaymentResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<VoidPaymentResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken);
    Task<SavedCardResult> SaveCardAsync(SaveCardRequest request, CancellationToken cancellationToken);
    Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionSearchItem>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
