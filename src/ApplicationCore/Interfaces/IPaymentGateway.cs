using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs used by this integration. Implemented in the
/// Infrastructure layer strictly against the PayPal OpenAPI specifications under
/// <c>api-specs/paypal/</c> (Checkout Orders v2, Payments v2, Vault Payment Tokens v3).
///
/// Every mutating operation accepts a caller-supplied idempotency key which the implementation
/// forwards as PayPal's <c>PayPal-Request-Id</c> header, so a retried call (e.g. a double-click)
/// never produces a duplicate charge or refund.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates and captures a PayPal Checkout order funded by raw card details (one-off payment).
    /// </summary>
    Task<GatewayPaymentResult> ChargeCardAsync(CardChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and captures a PayPal Checkout order funded by a previously vaulted card token.
    /// </summary>
    Task<GatewayPaymentResult> ChargeVaultedCardAsync(VaultedCardChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds a captured payment, identified by its PayPal capture id.
    /// </summary>
    Task<GatewayRefundResult> RefundAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a card in PayPal's vault and returns a reusable token plus a safe description.
    /// </summary>
    Task<GatewayVaultResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a vaulted card token from PayPal so it can no longer be used to pay.
    /// </summary>
    Task<bool> DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default);
}
