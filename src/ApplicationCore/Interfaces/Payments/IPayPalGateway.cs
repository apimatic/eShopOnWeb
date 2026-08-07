using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over PayPal used by the application layer. The concrete implementation lives in the
/// Infrastructure project and is the only place that references the PayPal SDK, keeping the domain and
/// application layers free of any payment-provider dependency.
///
/// Every operation that moves money or creates a resource takes an <c>idempotencyKey</c>, which the
/// implementation forwards to PayPal (PayPal-Request-Id) so a retried call cannot double-charge,
/// double-refund or double-vault.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Create + capture a PayPal order for the given amount using raw card data.</summary>
    Task<PayPalCaptureResult> CaptureWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Create + capture a PayPal order for the given amount using a previously vaulted card token.</summary>
    Task<PayPalCaptureResult> CaptureWithVaultedCardAsync(decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card so it can be charged later without re-entering it.</summary>
    Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card from PayPal. Safe to call for a token that is already gone.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Fully refund a previously captured payment.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default);
}
