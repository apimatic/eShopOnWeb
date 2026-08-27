using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ServiceResult<ContactNumberView>> RegisterContactNumberAsync(string buyerId, string phoneNumber,
        string? countryCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<ServiceResult<OrderView>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput>? items,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken);
    Task<ServiceResult<OrderView>> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<ServiceResult<OrderView>> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<NotificationView>>> GetNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken);
    Task<ServiceResult<NotificationView>> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeleteContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ServiceResult<ReconciliationView>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public enum ServiceFailure { None, NotFound, Invalid, Conflict, ProviderUnavailable }

public sealed record ServiceResult<T>(T? Value, ServiceFailure Failure = ServiceFailure.None, string? Error = null)
{
    public bool Succeeded => Failure == ServiceFailure.None;
    public static ServiceResult<T> Success(T value) => new(value);
    public static ServiceResult<T> Fail(ServiceFailure failure, string error) => new(default, failure, error);
}

public sealed record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderView(int OrderId, OrderStatus Status, DateTimeOffset OrderDate, decimal Total,
    IReadOnlyList<OrderLineView> Items, IReadOnlyList<NotificationSummaryView> Notifications);
public sealed record NotificationSummaryView(int NotificationId, NotificationKind Kind, string ProviderStatus,
    string? ProviderMessageSid, DateTimeOffset? ScheduledFor);
public sealed record NotificationView(int NotificationId, int OrderId, NotificationKind Kind, string? Content,
    bool ContentDeleted, string? ProviderMessageSid, string ProviderStatus, int? ProviderErrorCode,
    string? ProviderErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderSentAt, DateTimeOffset? ProviderUpdatedAt, int? OriginalNotificationId);
public sealed record ReconciliationItemView(string? ProviderMessageSid, bool InProvider, bool InApplication,
    int? NotificationId, int? OrderId, string? ProviderStatus, string? ApplicationStatus, DateTimeOffset? ProviderDate);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationItemView> Messages);
