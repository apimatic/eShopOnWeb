using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order against PayPal: authorize (hold), capture at
/// fulfilment, void on cancel, and refund. Every operation is idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Authorizes (places a hold for) the order total for the given shopper, using either one-off
    /// card details or one of the shopper's saved cards. Scoped to the caller's own order.
    /// </summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order, capturing the held funds. Renews a stale hold rather than failing outright.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds the captured payment, fully (null amount) or partially. Idempotent on the caller's key.
    /// Returns the order with its updated payment; the resulting refund is found via
    /// <see cref="Payment.FindRefundByKey"/> using the same key.
    /// </summary>
    Task<Order> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
