using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// PayPal gateway operations used by checkout. Implementations must follow the
/// PayPal OpenAPI documents under api-specs/ (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1, and OAuth2 client-credentials).
/// </summary>
public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<RefundGatewayResult> RefundCaptureAsync(
        string captureId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<TransactionSearchResult> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
