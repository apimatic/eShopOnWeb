using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber,
        string? countryCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumberResult>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken = default);
    Task<OrderResult> PlaceOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);
    Task<OrderResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderResult>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationResult>?> GetNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default);
    Task<NotificationResult?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record ContactNumberResult(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record OrderItemResult(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderResult(int OrderId, string Status, DateTimeOffset OrderDate, decimal Total,
    IReadOnlyList<OrderItemResult> Items, IReadOnlyList<NotificationResult> Notifications);
public sealed record NotificationResult(int NotificationId, int OrderId, NotificationKind Kind,
    string? Content, string? ProviderMessageId, string ProviderStatus, int? ProviderErrorCode,
    string? ProviderErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt, DateTimeOffset? ContentDisposedAt, int? SourceNotificationId);
public sealed record ReconciliationEntry(string ProviderMessageId, string Presence, string ProviderStatus,
    int? NotificationId, int? OrderId, DateTimeOffset ProviderCreatedAt, DateTimeOffset? ProviderSentAt);
public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);

public class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}

public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}

public class NotificationProviderException : Exception
{
    public NotificationProviderException(string message) : base(message) { }
    public NotificationProviderException(string message, Exception innerException) : base(message, innerException) { }
}
