using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-scoped ordering: placing an order from catalog items (reusing the app's existing order model)
/// and reading the caller's orders with the state their notifications reached.
/// </summary>
public interface IShopperOrderService
{
    /// <summary>
    /// Place an order for the caller from catalog item ids + quantities, then tell them it was placed.
    /// Returns the new order id. Throws <see cref="InvalidOrderRequestException"/> when the request is empty
    /// or references an unknown catalog item.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct);

    /// <summary>The caller's orders, each with the current state of its notifications.</summary>
    Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// One of the caller's orders with what was sent for it and what became of each message.
    /// Returns null when the order is not the caller's / does not exist.
    /// </summary>
    Task<OrderNotificationsView?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);
}

/// <summary>One requested order line.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order plus the notifications sent about it.</summary>
public sealed record OrderNotificationsView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>A single notification and what became of it — carries its own notification id.</summary>
public sealed record NotificationView(
    int NotificationId,
    NotificationKind Kind,
    NotificationDeliveryState DeliveryState,
    string? ProviderStatus,
    string? MessageSid,
    int? ProviderErrorCode,
    string? DateSent,
    DateTimeOffset? ScheduledSendAt,
    bool ContentRedacted);
