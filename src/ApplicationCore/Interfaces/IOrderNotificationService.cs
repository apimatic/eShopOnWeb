using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken ct);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken ct);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct);
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, Address shippingAddress, CancellationToken ct);
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct);
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);
    Task<IReadOnlyList<ReconciliationView>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task RetryPendingCancellationsAsync(CancellationToken ct);
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredAt);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total, IReadOnlyList<NotificationView> Notifications);
public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string DeliveryStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    bool ContentDisposed,
    bool CancellationPending,
    DateTimeOffset? LastProviderSyncAt,
    int? OriginalNotificationId);
public sealed record ReconciliationView(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationStatus,
    string? ProviderDateCreated,
    DateTimeOffset? ApplicationCreatedAt);

public sealed class ContactNumberValidationException(string message) : Exception(message);
public sealed class OrderRequestValidationException(string message) : Exception(message);
public sealed class NotificationActionException(string message) : Exception(message);
public sealed class NotificationProviderException(string message, int? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}
