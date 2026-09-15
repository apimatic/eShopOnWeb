using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single seam between this app and PayPal. Every method wraps one PayPal operation, translating
/// SDK success/failure into plain domain types and <see cref="Exceptions.PayPalProviderException"/>s,
/// so the rest of the app never depends on the SDK. Idempotency keys are passed through to PayPal as
/// PayPal-Request-Id.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>The currency (ISO-4217) all amounts are denominated in, from configuration (PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>Place a hold on <paramref name="amount"/> using raw card details. Does not take the money.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Place a hold on <paramref name="amount"/> using a previously vaulted card. The owning PayPal
    /// customer id (captured when the card was vaulted) is replayed so PayPal permits the charge.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string vaultId, string? payPalCustomerId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture (take) the money for a previously authorized payment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renew a stale authorization so it can be captured.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct = default);

    /// <summary>Void an authorization before capture, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refund a capture in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Vault (save) a card under a stable customer id and return its token, owning customer id and a safe
    /// description. The <paramref name="merchantCustomerId"/> is the app's per-shopper id.
    /// </summary>
    Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// PayPal's own record of transactions over an ISO-8601 date-time range, paged over the whole range.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(string startDate, string endDate,
        CancellationToken ct = default);
}
