using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Keeps provider/SDK types out of the domain and
/// the API. Every method is idempotent in effect via a caller-supplied <c>idempotencyKey</c>: the
/// provider replays the original outcome for a repeated key instead of charging or refunding twice.
///
/// Implementations translate all provider and transport failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentGatewayException"/> with a
/// caller-safe message. Full card details passed in <see cref="CardDetails"/> are used only to reach
/// the provider — never stored or logged.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Charge a one-off card payment (create + capture) for the given amount.</summary>
    Task<PaymentCaptureResult> ChargeCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Charge a previously vaulted card by its vault id.</summary>
    Task<PaymentCaptureResult> ChargeVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment in full.</summary>
    Task<PaymentRefundResult> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card and return its id plus a safe descriptor for display.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card from the provider so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
