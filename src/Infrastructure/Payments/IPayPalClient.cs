using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentReference, decimal amount, CardData? card,
        string? paymentTokenId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, string paymentReference, decimal amount,
        string requestId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalVaultToken> SaveCardAsync(CardData card, string? customerId,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
