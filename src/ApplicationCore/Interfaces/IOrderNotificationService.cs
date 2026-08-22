using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);

public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string? LocalStatus,
    string? ProviderStatus,
    DateTimeOffset? DateCreated);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly);

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        ShippingAddressDto? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
