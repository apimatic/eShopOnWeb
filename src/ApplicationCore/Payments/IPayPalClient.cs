using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency,
        string externalReference, string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string merchantCustomerId,
        string setupRequestId, string tokenRequestId, CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
