using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives their PayPal payment lifecycle (pay / refund / query) for a single
/// authenticated shopper. All operations are scoped to <c>buyerId</c> so one shopper can never act on
/// another's orders.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places a new order for the shopper from catalog items. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IEnumerable<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays for the shopper's order via PayPal using either a one-off <paramref name="card"/> or one of
    /// the shopper's <paramref name="savedPaymentMethodId"/> cards. Idempotent: a repeated call for an
    /// already-paid order returns the order unchanged rather than charging again.
    /// </summary>
    Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds the shopper's paid order. Idempotent: a repeated call for an already-refunded order
    /// returns the order unchanged rather than refunding again.
    /// </summary>
    Task<Order> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's orders, each with its payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
