using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public interface IPayPalService
{
    string Currency { get; }

    Task<AuthorizeResult> AuthorizeOrderAsync(
        decimal amount, CardPaymentDetails card, CancellationToken ct = default);

    Task<AuthorizeResult> AuthorizeOrderWithTokenAsync(
        decimal amount, string vaultTokenId, CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(
        string authorizationId, CancellationToken ct = default);

    Task VoidAuthorizationAsync(
        string authorizationId, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<VaultResult> VaultCardAsync(
        string customerId, string? existingPayPalCustomerId, CardPaymentDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<VaultedCardInfo>> ListVaultedCardsAsync(
        string payPalCustomerId, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(
        string tokenId, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(
        string startDate, string endDate, CancellationToken ct = default);
}
