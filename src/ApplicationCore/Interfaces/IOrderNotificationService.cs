using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and keeps the shopper informed by SMS as the order moves. A message that cannot be
/// sent never fails the underlying operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order from catalog items for the given shopper (reusing the app's Order/OrderItem model)
    /// and tells the shopper it was placed. Returns the new order's id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with the
    /// provider for a few days later asking how the delivery went. Returns false if the order is not found.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any not-yet-sent follow-up so it never reaches
    /// them. Returns false if the order is not found.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's notifications for the given order ids, with delivery outcomes refreshed from the
    /// provider. Used to show where each order's notifications got to.
    /// </summary>
    Task<IReadOnlyList<SmsNotification>> GetNotificationsForOrdersAsync(string buyerId,
        IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one of the caller's orders, refreshed from the provider. Returns null when the
    /// order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<SmsNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default);
}
