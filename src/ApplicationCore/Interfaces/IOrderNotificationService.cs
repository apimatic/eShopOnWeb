using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Null when the order does not exist.</summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Null when the order does not exist.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What was sent for an order, with each message's outcome refreshed from the
    /// provider. Null when the order does not exist or is not the caller's (admins
    /// may view any order).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<ResendNotificationResult> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Null when the notification does not exist; false when the provider refused the redaction.</summary>
    Task<bool?> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record CatalogItemQuantity(int CatalogItemId, int Units);

public record ResendNotificationResult(OrderNotification? Notification, bool AlreadyExisted, string? Error)
{
    public bool Success => Notification is not null;

    public static ResendNotificationResult Failed(string error) => new(null, false, error);
}
