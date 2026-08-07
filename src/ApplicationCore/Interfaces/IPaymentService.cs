using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Pays for and refunds orders through PayPal. All operations enforce that the
/// order belongs to <c>buyerId</c> and are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Pays for an order with a one-off card. Returns the updated order.</summary>
    Task<Order> PayOrderWithCardAsync(
        int orderId, string buyerId, CardPaymentDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>Pays for an order using one of the shopper's saved cards.</summary>
    Task<Order> PayOrderWithSavedMethodAsync(
        int orderId, string buyerId, int savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund for an order's payment. Returns the updated order.</summary>
    Task<Order> RefundOrderAsync(
        int orderId, string buyerId, CancellationToken cancellationToken = default);
}
