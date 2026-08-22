using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<string> CreateAuthorizedOrderAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> AuthorizeWithCardAsync(
        string payPalOrderId,
        CardPaymentDetails card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> AuthorizeWithVaultIdAsync(
        string payPalOrderId,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string? invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<VaultedCardDetails> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
