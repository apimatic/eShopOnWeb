using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberDto> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberDto>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<int> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken);
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationDto>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationDto> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed record PlaceOrderCommand(IReadOnlyList<PlaceOrderItem> Items, ShippingAddress ShippingAddress);

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items,
    NotificationSummaryDto Notifications);

public sealed record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationSummaryDto(int Total, int Delivered, int Failed, int Pending, int Scheduled, int Cancelled);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string ProviderStatus,
    string? ProviderSid,
    int? ProviderErrorCode,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentDisposedAt,
    int? SourceNotificationId);

public sealed record ReconciliationDto(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryDto> Entries,
    int Matched,
    int ProviderOnly,
    int ApplicationOnly);

public sealed record ReconciliationEntryDto(
    string Alignment,
    string? ProviderSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);

public sealed class NotificationResourceNotFoundException : Exception
{
    public NotificationResourceNotFoundException(string message) : base(message) { }
}

public sealed class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}

public sealed class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}
