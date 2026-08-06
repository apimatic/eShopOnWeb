using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the external payment gateway (implemented against PayPal). Handles one-off card
/// charges, charging a previously vaulted card, saving/removing vaulted cards, and full refunds.
/// Every money-moving call takes an idempotency key so a retried request never double-charges or
/// double-refunds. Implementations must never log full card details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Charges a raw card for the given amount and captures immediately.</summary>
    Task<PaymentResult> ChargeCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Charges a previously vaulted card (by its vault id) and captures immediately.</summary>
    Task<PaymentResult> ChargeVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a raw card in the gateway vault for later reuse and returns its vault token and a safe
    /// descriptor. Pass an existing customer id to group a shopper's cards under one gateway customer.
    /// </summary>
    Task<VaultedCard> SaveCardAsync(CardDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of a captured payment.</summary>
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
