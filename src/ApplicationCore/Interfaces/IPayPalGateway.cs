using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? existingCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
