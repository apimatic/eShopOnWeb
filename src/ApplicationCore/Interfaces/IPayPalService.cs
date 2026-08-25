using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalService
{
    Task<PayPalAuthorizeResult> AuthorizeWithCardAsync(decimal amount, PayPalCardRequest card, CancellationToken ct = default);
    Task<PayPalAuthorizeResult> AuthorizeWithVaultAsync(decimal amount, string vaultId, CancellationToken ct = default);
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);
    Task<PayPalAuthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct = default);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, CancellationToken ct = default);
    Task VoidAsync(string authorizationId, CancellationToken ct = default);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string? idempotencyKey, CancellationToken ct = default);
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardRequest card, string merchantCustomerId, CancellationToken ct = default);
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default);
    Task<IReadOnlyList<PayPalTransactionRecord>> GetTransactionsAsync(string from, string to, CancellationToken ct = default);
}
