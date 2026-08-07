using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin gateway over the PayPal REST API. Implementations own authentication,
/// idempotency headers and error translation; callers work in domain terms.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates and captures a PayPal order paid directly with the supplied card.
    /// <paramref name="idempotencyKey"/> is sent as PayPal-Request-Id so repeated
    /// calls never double-charge.
    /// </summary>
    Task<PayPalPaymentResult> CreateCardOrderAsync(
        decimal amount, string currencyCode, CardPaymentDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and captures a PayPal order paid with a previously vaulted card.
    /// </summary>
    Task<PayPalPaymentResult> CreateVaultedCardOrderAsync(
        decimal amount, string currencyCode, string vaultId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund against a captured payment.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card for later reuse. When <paramref name="customerId"/> is null a
    /// new PayPal customer is created; otherwise the card is added under that customer.
    /// </summary>
    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card, string? customerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card from PayPal so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
