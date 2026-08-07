using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). The application core depends only on this
/// interface and the plain DTOs in <see cref="Payments"/>; the concrete PayPal SDK integration
/// lives in the Infrastructure project.
///
/// Every method is idempotent in effect: callers pass a stable <c>idempotencyKey</c> so that a
/// retried or double-clicked request never produces a second charge or refund at the provider.
/// Failures surface as <see cref="Exceptions.PaymentException"/>.
/// </summary>
public interface IPaymentService
{
    /// <summary>Charge a one-off card for an order and capture the funds.</summary>
    Task<CardPaymentResult> ChargeOrderWithCardAsync(PaymentAmount amount, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Charge one of the shopper's vaulted cards (by its PayPal vault token) for an order.</summary>
    Task<CardPaymentResult> ChargeOrderWithVaultedCardAsync(PaymentAmount amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Fully refund a captured payment.</summary>
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card with PayPal for reuse, returning a safe descriptor and the vault token.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card from PayPal so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
