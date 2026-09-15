using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order against PayPal: authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each operation is idempotent in effect and scoped to its owner/operator.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Authorize the order total for the owning shopper — either with one-off card details or one of the
    /// shopper's saved cards. Idempotent: a repeat once authorized returns the existing hold.
    /// </summary>
    Task<Payment> AuthorizeAsync(int orderId, string buyerId, PayPalCardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator fulfilment: capture the held funds. Renews a stale authorization before capturing; if it
    /// can no longer be renewed, throws <see cref="Exceptions.AuthorizationExpiredException"/>.
    /// Idempotent: a repeat once captured returns the existing capture.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancellation before fulfilment: release the hold. Idempotent once canceled.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment, full or partial, guarded by a caller-supplied idempotency key and by the
    /// remaining refundable balance. A non-admin caller may only refund an order they own.
    /// </summary>
    Task<Refund> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        string requesterBuyerId, bool requesterIsAdmin, CancellationToken cancellationToken = default);
}
