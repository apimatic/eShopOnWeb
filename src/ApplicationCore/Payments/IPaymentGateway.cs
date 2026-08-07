using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Abstraction over the PayPal payment provider. The application core depends only on this port; the
/// concrete implementation (built to PayPal's OpenAPI specs) lives in the Infrastructure layer.
/// Implementations throw <see cref="PaymentGatewayException"/> when PayPal rejects a request.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Charges a payment for an order (creates and captures a PayPal Orders v2 order), using either a
    /// one-off card or a previously vaulted card. Idempotent via the request's idempotency key.
    /// </summary>
    Task<PaymentResult> ChargeAsync(ChargeCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of a previously captured payment. Idempotent via the idempotency key.</summary>
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card so it can be reused later, associated with the shopper identified by
    /// <paramref name="buyerReference"/> (the implementation derives a PayPal-safe customer id from it).
    /// Returns the vault token id plus safe-to-display descriptors. Full card details are not persisted here.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string buyerReference, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card from PayPal so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);
}
