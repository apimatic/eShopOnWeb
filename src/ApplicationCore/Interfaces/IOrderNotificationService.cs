using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<int> RegisterContactNumberAsync(string shopperId, string number, CancellationToken ct);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string shopperId, CancellationToken ct);
    Task<bool> DeleteContactNumberAsync(string shopperId, int contactNumberId, CancellationToken ct);
    Task<int> PlaceOrderAsync(string shopperId, Address shippingAddress, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct);
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct);
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string shopperId, CancellationToken ct);
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string shopperId, int orderId, CancellationToken ct);
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ContactNumberView(int ContactNumberId, string Number);
public sealed record NotificationView(int NotificationId, string Kind, string DeliveryStatus,
    string? ProviderMessageSid, int? ProviderErrorCode, string? Content,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ContentDisposedAt);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<NotificationView> Notifications);
public sealed record ReconciliationEntry(string ProviderMessageSid, int? NotificationId,
    string Presence, string? ProviderStatus, string? LocalStatus, int? ProviderErrorCode,
    DateTimeOffset? ProviderCreatedAt, DateTimeOffset? ProviderSentAt);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
