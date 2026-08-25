using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay / fulfil / cancel / refund lifecycle of an order's payment against
/// PayPal, keeping the Order and Payment aggregates and PayPal's own state in sync.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Authorizes (holds) the order total. Exactly one of <paramref name="card"/> or
    /// <paramref name="savedPaymentMethodId"/> must be supplied. Idempotent: calling this again
    /// on an already-authorized order returns the existing authorization without a new hold.
    /// </summary>
    Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>
    /// Captures the held authorization (taking the money). Transparently renews a stale
    /// authorization via PayPal's reauthorize operation before capturing.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Cancels an order before fulfilment, voiding any authorization hold so no money moves.
    /// </summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Refunds a fulfilled order's capture, in full (amount == null) or in part. Repeating a
    /// call with the same idempotencyKey returns the original refund rather than refunding twice.
    /// </summary>
    Task<Refund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);
}
