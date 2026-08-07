using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal for the payment operations this app needs. The concrete implementation
/// (Infrastructure) is the only code that talks to the PayPal SDK; everything else depends on this
/// interface, which keeps the SDK out of the domain and makes the payment flow testable.
///
/// Every write takes an <c>idempotencyKey</c> that is stable per logical action, so a retried or
/// double-clicked request never produces a second charge or refund.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Charges a raw card once (create PayPal order with intent=CAPTURE, then capture).</summary>
    Task<PaymentCaptureResult> ChargeCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Charges a previously vaulted card, referenced by its PayPal vault-token id.</summary>
    Task<PaymentCaptureResult> ChargeVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a raw card and returns a reusable token id plus a safe descriptor (brand + last4).</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full, referenced by its capture id.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
