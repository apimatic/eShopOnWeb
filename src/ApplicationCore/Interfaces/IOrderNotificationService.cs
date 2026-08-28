using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberResult>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken);
    Task<PlaceOrderResult?> PlaceOrderAsync(string buyerId, ShippingAddressInput address,
        IReadOnlyList<OrderLineInput> items, CancellationToken cancellationToken);
    Task<OperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<OperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderResult>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationResult>?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken);
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<OperationResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record ContactNumberResult(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record PlaceOrderResult(int OrderId, IReadOnlyList<int> NotificationIds);
public sealed record OrderLineResult(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderResult(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<OrderLineResult> Items, IReadOnlyList<NotificationResult> Notifications);
public sealed record NotificationResult(int NotificationId, int OrderId, string Kind, string? Content,
    string? ProviderMessageSid, string ProviderStatus, int? ProviderErrorCode, DateTimeOffset CreatedAt,
    DateTimeOffset? ProviderDateSent, DateTimeOffset? ScheduledFor, DateTimeOffset? LastProviderSyncAt,
    DateTimeOffset? ContentDisposedAt, int? SourceNotificationId);
public sealed record ResendResult(bool Succeeded, int? NotificationId, string? Error);
public sealed record OperationResult(bool Succeeded, string? Error = null);
public sealed record ReconciliationEntry(string ProviderMessageSid, string ProviderStatus,
    DateTimeOffset? ProviderDateSent, int? NotificationId, string MatchState);
public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
