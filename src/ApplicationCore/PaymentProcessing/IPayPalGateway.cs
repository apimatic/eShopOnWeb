using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Port over the PayPal REST API as described by the OpenAPI specs under
/// <c>api-specs/paypal/</c>. The concrete implementation lives in the Infrastructure layer and
/// is the only code that speaks HTTP to PayPal; the rest of the app depends on this abstraction.
///
/// Every mutating call takes an <c>idempotencyKey</c> which the implementation sends as the
/// <c>PayPal-Request-Id</c> header so a retried/double-clicked call never produces a second
/// charge or refund.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal Checkout order with <c>intent=CAPTURE</c> and the given card payment
    /// source, capturing the funds in a single call (spec: POST /v2/checkout/orders).
    /// </summary>
    Task<CaptureResult> CreateAndCaptureOrderAsync(
        decimal amount,
        string currencyCode,
        CardPaymentSource source,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds a captured payment (spec: POST /v2/payments/captures/{capture_id}/refund).
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card so it can be charged later by <c>vault_id</c>
    /// (spec: POST /v3/vault/payment-tokens). When <paramref name="customerId"/> is supplied the
    /// token is grouped under that existing PayPal customer.
    /// </summary>
    Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string? customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a vaulted card so it can no longer be used to pay
    /// (spec: DELETE /v3/vault/payment-tokens/{id}).
    /// </summary>
    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);
}
