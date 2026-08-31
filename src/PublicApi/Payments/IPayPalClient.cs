using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<AuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<VaultResult> SaveCardAsync(CardInput card, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
