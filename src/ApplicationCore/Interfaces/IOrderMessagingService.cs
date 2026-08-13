using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and drives the SMS notifications that go out as an order moves
/// through its lifecycle. A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderMessagingService
{
    /// <summary>Places an order for the buyer from catalog lines and notifies them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks an order dispatched, tells the shopper, and queues a delivery follow-up for later. Operator action.</summary>
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an order, tells the shopper, and calls off any not-yet-sent follow-up. Operator action.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Fetches an order that must belong to the buyer, or throws <see cref="Exceptions.NotFoundException"/>.</summary>
    Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifications for the given orders, with each one's delivery outcome refreshed from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper, idempotent on the caller's key. Operator action.</summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the provider and locally, keeping the record. Operator action.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's message ledger against eShop's records over a date range. Operator action.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One requested catalog line on a new order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>The provider's ledger lined up against eShop's records for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InProviderNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInProvider);

/// <summary>A single reconciled message, identified by its provider SID.</summary>
public record ReconciliationEntry(string Sid, string? ProviderStatus, DateTimeOffset? DateSent, int? NotificationId, int? OrderId, string? EShopStatus);
