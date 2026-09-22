using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal for the payment flows. The only seam through which the app talks to the
/// PayPal SDK, so PayPal types never leak into ApplicationCore or PublicApi. All methods translate
/// PayPal failures to <see cref="Exceptions.PayPalGatewayException"/>.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Authorizes (holds) the order total against a one-off or vaulted card. Does not capture.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct);

    /// <summary>Captures the full authorized amount — this is when money moves.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string currency, CancellationToken ct);

    /// <summary>Reads the current status/expiry of an authorization.</summary>
    Task<AuthorizationStatusResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization so it can be captured. Throws <see cref="Exceptions.PayPalGatewayException"/>
    /// with kind <see cref="Exceptions.PaymentGatewayErrorKind.AuthorizationNotRenewable"/> when it cannot be renewed.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, CancellationToken ct);

    /// <summary>Voids an authorization before capture, releasing the shopper's held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refunds a captured payment — full when <paramref name="amount"/> is null, otherwise partial.
    /// <paramref name="idempotencyKey"/> is sent to PayPal so a repeat under the same key does not refund twice.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Re-reads a PayPal order to recover the capture after an ambiguous capture request. Null if none present.</summary>
    Task<CaptureLookupResult?> FindCaptureByPayPalOrderAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Vaults a card and returns its token id plus safe descriptors (never the card number).</summary>
    Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct);

    /// <summary>Removes a vaulted card from PayPal so it can no longer fund a payment.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Lists PayPal's own transaction records for a date range, walking every page.</summary>
    Task<ReconciliationSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
