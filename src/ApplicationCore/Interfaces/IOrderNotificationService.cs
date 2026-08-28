using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string number, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken);
    Task RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput? address, CancellationToken cancellationToken);
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<int> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task RetryPendingCancellationsAsync(CancellationToken cancellationToken);
}

public sealed record ContactNumberView(int ContactNumberId, string Number, DateTimeOffset CreatedAt);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string DeliveryStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? Content,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LastProviderCheckAt,
    int? SourceNotificationId,
    bool ProviderCancellationPending);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<NotificationView> Notifications);

public sealed record ReconciliationEntry(
    string ReconciliationStatus,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? NotificationType,
    string? ProviderStatus,
    string? To,
    DateTimeOffset? ProviderDateCreated,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);

public sealed record ReconciliationView(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int ProviderOnlyCount,
    int ApplicationOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
