using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Domain-facing abstraction over PayPal. Implementations translate these operations into calls
/// against the PayPal REST APIs described by the OpenAPI specs under <c>api-specs/paypal/</c>.
/// All money is captured immediately (PayPal intent CAPTURE); amounts are in the given currency.
/// Every operation that can be replayed accepts an idempotency key that is forwarded to PayPal as
/// the <c>PayPal-Request-Id</c> header so a retried call never charges or refunds twice.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal Checkout order for <paramref name="amount"/> and captures it in one step,
    /// paying with either raw card details or a vaulted card.
    /// </summary>
    Task<CapturedPayment> CaptureCardPaymentAsync(
        decimal amount,
        string currencyCode,
        CardPaymentSource source,
        string idempotencyKey,
        string orderReference,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture in full.</summary>
    Task<RefundOutcome> RefundCaptureAsync(
        string captureId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a card in PayPal's vault and returns its token plus a safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(
        CardDetails card,
        string customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Permanently removes a vaulted card so it can no longer be charged.</summary>
    Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
