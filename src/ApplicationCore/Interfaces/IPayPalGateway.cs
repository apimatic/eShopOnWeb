using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs used by this integration. The concrete
/// implementation lives in the Infrastructure layer and speaks to PayPal strictly
/// according to the OpenAPI specifications under api-specs/paypal/.
///
/// Every method takes an idempotency key that maps to PayPal's PayPal-Request-Id
/// header so that retries (e.g. a double-click) never produce a duplicate charge or refund.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates and captures a PayPal order paying with the supplied raw card
    /// (PayPal Checkout Orders v2, intent=CAPTURE).
    /// </summary>
    Task<PayPalChargeResult> ChargeWithCardAsync(
        Money amount, CardPaymentDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and captures a PayPal order paying with a previously vaulted card
    /// (payment_source.card.vault_id).
    /// </summary>
    Task<PayPalChargeResult> ChargeWithVaultedCardAsync(
        Money amount, string vaultToken, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a full refund of a capture (PayPal Payments v2). A full refund omits the amount.
    /// </summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a raw card as a reusable payment token for the given PayPal customer
    /// (PayPal Vault v3). Pass a null customer id to have PayPal create one and return it.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(
        CardPaymentDetails card, string? existingCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card by its payment-token id (PayPal Vault v3).</summary>
    Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default);
}
