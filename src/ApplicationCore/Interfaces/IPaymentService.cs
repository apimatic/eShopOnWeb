using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order payment lifecycle: authorize (hold) at checkout,
/// capture at fulfilment, void on cancel, refund on return.
/// All operations are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Put a hold on the order total, either with raw card details or with one of the
    /// shopper's saved cards. Repeating the call for an already-authorized order
    /// returns the existing authorization instead of charging twice.
    /// </summary>
    Task<Payment> PayAsync(string buyerId, int orderId, PayPalCardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture the held funds (operator action at fulfilment). Renews a stale
    /// authorization first; throws <see cref="Exceptions.PaymentDomainException"/>
    /// with an operator-actionable message when it can no longer be renewed.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release the shopper's held funds before fulfilment (operator action).
    /// Returns null when the order was never paid (nothing to release at PayPal).
    /// </summary>
    Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment, in full or in part. The caller-supplied idempotency
    /// key guarantees a repeated request never refunds twice; distinct keys allow
    /// multiple legitimate partial refunds up to the captured amount.
    /// </summary>
    Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string? noteToPayer, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
