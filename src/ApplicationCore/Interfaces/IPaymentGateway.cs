using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Keeps the application core independent of the
/// concrete HTTP/SDK details. Implementations must never persist or log raw card data.
///
/// Every mutating call takes an <c>idempotencyKey</c> that the implementation forwards to the
/// processor so that a retried/double-clicked request never results in a duplicate charge, vault or
/// refund.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Charges a one-off card payment for the given amount (create + capture a PayPal order).
    /// </summary>
    Task<CardChargeResult> ChargeCardAsync(
        decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Charges a previously-vaulted (saved) card for the given amount.
    /// </summary>
    Task<CardChargeResult> ChargeVaultedCardAsync(
        decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (vaults) a card in the processor and returns its token plus a safe display summary.
    /// </summary>
    Task<VaultedCardResult> VaultCardAsync(
        CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a full refund of a previously captured payment.
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort deletion of a vaulted card from the processor. Never throws; failures are logged
    /// and swallowed because the card is already unusable once removed from the application's own store.
    /// </summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
