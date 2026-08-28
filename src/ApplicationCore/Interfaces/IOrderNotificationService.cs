using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string number, string? countryCode, CancellationToken ct);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken ct);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct);
    Task<int> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken ct);
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct);
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record ContactNumberView(int ContactNumberId, string Number, DateTimeOffset CreatedAt);
public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);
public sealed record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderCommand(IReadOnlyList<PlaceOrderLine> Items, ShippingAddress ShippingAddress);
public sealed record NotificationView(int NotificationId, NotificationKind Kind, string Status, string? Content,
    string? ProviderMessageId, int? ProviderErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor,
    DateTimeOffset? ContentDisposedAt, int? ResendsNotificationId);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, string Progress, decimal Total,
    IReadOnlyList<NotificationView> Notifications);
public sealed record ReconciliationEntry(string ProviderMessageId, int? NotificationId, string Side,
    string Status, DateTimeOffset? DateSent);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
