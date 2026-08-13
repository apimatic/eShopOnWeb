using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineItem(int CatalogItemId, int Quantity);

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. Each action is
/// separately invocable. A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog items, reusing the app's Order/OrderItem model,
    /// and tells the shopper it was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> items, CancellationToken ct = default);

    /// <summary>
    /// Marks an order dispatched, tells the shopper it is on its way, and queues a "how did delivery
    /// go?" follow-up with the provider for a few days later. (Operator action.)
    /// </summary>
    Task DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Cancels an order, tells the shopper, and calls off any follow-up the provider is still holding
    /// so it never reaches them. (Operator action.)
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>The shopper's own orders.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// All of the shopper's notifications, with each message's delivery outcome refreshed from the
    /// provider. Callers group these by <see cref="Notification.OrderId"/>.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>An order by id (for ownership checks); null if not found.</summary>
    Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// The notifications raised for an order, with each message's delivery outcome refreshed from the
    /// provider.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetNotificationsForOrderAsync(int orderId, CancellationToken ct = default);
}
