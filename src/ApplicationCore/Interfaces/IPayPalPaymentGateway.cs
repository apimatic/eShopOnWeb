using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentGateway
{
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(PayPalCardDetails card, decimal amount, string requestId, CancellationToken ct = default);
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultId, decimal amount, string requestId, CancellationToken ct = default);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct = default);
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default);
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string requestId, CancellationToken ct = default);
    Task<PayPalVaultedCard> SaveCardAsync(PayPalCardDetails card, string customerId, string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<PayPalVaultedCard>> ListSavedCardsAsync(string customerId, CancellationToken ct = default);
    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
