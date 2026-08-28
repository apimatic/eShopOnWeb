using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string input, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken);
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record NotificationProgress(string Kind, string DeliveryStatus, string? ProviderStatus, int Count);
public sealed record OrderSummary(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<NotificationProgress> Notifications);
public sealed record ReconciliationEntry(string ProviderMessageSid, string Match, int? NotificationId,
    string? ProviderStatus, string? LocalStatus, DateTimeOffset? ProviderTimestamp, string? To, string? Body);

public sealed class OrderNotificationValidationException : Exception
{
    public OrderNotificationValidationException(string message) : base(message) { }
}

public sealed class OrderNotificationConflictException : Exception
{
    public OrderNotificationConflictException(string message) : base(message) { }
}
