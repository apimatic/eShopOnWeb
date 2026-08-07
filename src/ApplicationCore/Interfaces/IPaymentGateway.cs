using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external payment processor (PayPal). Implementations own all HTTP/API detail;
/// the application core only deals in amounts, cards, vault tokens and gateway ids.
/// All operations accept an <c>idempotencyKey</c> so a retried/duplicated request never results in a
/// duplicate charge or refund at the gateway.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Charges a one-off card for <paramref name="amount"/> and returns the capture details.</summary>
    Task<GatewayChargeResult> ChargeCardAsync(
        decimal amount, string currencyCode, PaymentCard card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Charges a previously vaulted card (by its vault token) for <paramref name="amount"/>.</summary>
    Task<GatewayChargeResult> ChargeVaultedCardAsync(
        decimal amount, string currencyCode, string vaultToken, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of a previously captured payment.</summary>
    Task<GatewayRefundResult> RefundAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a card in the gateway's vault and returns a reusable token plus safe descriptors.
    /// <paramref name="customerId"/>, when supplied, groups the card under an existing vault customer.
    /// </summary>
    Task<GatewaySavedCard> VaultCardAsync(
        PaymentCard card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default);
}
