using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(string externalReference, decimal amount, string currency,
        PayPalPaymentSource source, CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string externalReference, string authorizationId,
        decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string externalReference, string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken);
    Task VoidAsync(string externalReference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string requestId, string captureId, decimal amount, string currency,
        CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(string requestId, string customerId, PaymentCardData card,
        CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
