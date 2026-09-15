using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalGateway
{
    Task<ProviderAuthorization> AuthorizeAsync(string externalReference, int orderId, decimal amount, string currency,
        ProviderCard? card, string? vaultId, CancellationToken cancellationToken);
    Task<ProviderCapture> CaptureAsync(string externalReference, int orderId, string authorizationId, decimal amount,
        string currency, DateTimeOffset? authorizationCreatedAt, CancellationToken cancellationToken);
    Task<ProviderVoid> VoidAsync(string externalReference, string authorizationId, CancellationToken cancellationToken);
    Task<ProviderRefund> RefundAsync(string externalReference, int orderId, string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<ProviderPaymentMethod> SavePaymentMethodAsync(string buyerId, ProviderCard card,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string vaultId, CancellationToken cancellationToken);
    Task<ProviderTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
