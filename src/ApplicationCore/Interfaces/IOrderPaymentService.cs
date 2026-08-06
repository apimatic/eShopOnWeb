using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Pays for and refunds an order through the payment gateway, idempotently and owner-scoped.</summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Pays for the shopper's order, either with one-off card details or one of the shopper's saved cards
    /// (exactly one of <paramref name="card"/> / <paramref name="savedPaymentMethodId"/> must be supplied).
    /// Idempotent: an already-paid order is returned unchanged. Returns null if the order does not exist
    /// or does not belong to the shopper.
    /// </summary>
    Task<Order?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds the shopper's paid order. Idempotent: an already-refunded order is returned unchanged.
    /// Returns null if the order does not exist or does not belong to the shopper.
    /// </summary>
    Task<Order?> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
